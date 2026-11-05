namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Creates an isolated real-provider tree before any tracked recovery state is loaded.</summary>
    private async Task<TestDatabase> CreateTableSharingRecoveryDatabaseAsync(
        string shape,
        CancellationToken cancellationToken
    )
    {
        var database = await TestDatabase.CreateAsync(Engine, cancellationToken: cancellationToken);

        try
        {
            await using var context = await CreateTableSharingRecoveryContextAsync(database, shape,
                cancellationToken: cancellationToken);

            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
            var tree = context.NestedSet<TableSharingRecoveryNode>();
            await tree.InsertRootAsync(NewTableSharingRecoveryNode(shape, 1, "Root"), s_treeId,
                cancellationToken);

            await tree.InsertChildAsync(NewTableSharingRecoveryNode(shape, 2, "Alpha"), 1, cancellationToken);
            await tree.InsertChildAsync(NewTableSharingRecoveryNode(shape, 3, "Bravo"), 1, cancellationToken);

            if (shape == "split")
            {
                // WHY: Non-owned navigation inputs are intentionally excluded from facade insertion. Populate
                // optional peer presence directly, without adding test-only exceptions to the import contract.
                var sql = context.GetService<ISqlGenerationHelper>();
                var update = $"UPDATE {sql.DelimitIdentifier("NavigationSaveNodes")} "
                    + $"SET {sql.DelimitIdentifier("PeerValue")} = {{0}}";

                await context.Database.ExecuteSqlRawAsync(update, ["original peer"], cancellationToken);
            }

            return database;
        }
        catch
        {
            // WHY: Failed setup must release its database before the test can take ownership of that resource.
            await database.DisposeAsync();

            throw;
        }
    }

    /// <summary>Creates owned insertion graphs; ordinary split peers are seeded after the principal rows.</summary>
    private static TableSharingRecoveryNode NewTableSharingRecoveryNode(string shape, int id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Owned = shape == "owned" ? new TableSharingRecoveryOwned { Id = id } : null,
        };

    /// <summary>Reuses provider options with separate EF model cache entries for the three shapes.</summary>
    private static async Task<TableSharingRecoveryContext> CreateTableSharingRecoveryContextAsync(
        TestDatabase database,
        string shape,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    )
    {
        var options = new DbContextOptionsBuilder(await SaveSemanticsFixture.OptionsAsync(database,
            cancellationToken: cancellationToken,
            interceptors))
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>().Options;

        return new TableSharingRecoveryContext(options, shape);
    }

    /// <summary>Loads the actual sharing peer before marking only the principal's order property.</summary>
    private static async Task<TableSharingRecoveryGraph> PrepareTableSharingRecoveryAsync(
        TableSharingRecoveryContext context,
        string shape,
        CancellationToken cancellationToken
    )
    {
        var query = context.Set<TableSharingRecoveryNode>().AsQueryable();

        if (shape == "owned")
        {
            query = query.Include(node => node.Owned);
        }
        else if (shape == "split")
        {
            query = query.Include(node => node.Peer);
        }

        var node = await query.SingleAsync(value => value.Id == 2, cancellationToken);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var entry = context.Entry(node);
        var name = entry.Property(value => value.Name);
        object? peer = shape == "owned" ? node.Owned : node.Peer;
        var peerEntry = peer is null ? null : context.Entry(peer);
        node.Name = "Zulu";
        name.IsModified = true;

        return new TableSharingRecoveryGraph(node, entry, peerEntry);
    }

    /// <summary>Changes only the stored token to cause genuine optimistic concurrency in the EF UPDATE.</summary>
    private static async Task ChangeTableSharingStoredVersionAsync(
        TestDatabase database,
        string shape,
        CancellationToken cancellationToken
    )
    {
        await using var external = await CreateTableSharingRecoveryContextAsync(database, shape,
            cancellationToken: cancellationToken);

        var sql = external.GetService<ISqlGenerationHelper>();
        var update = $"UPDATE {sql.DelimitIdentifier("NavigationSaveNodes")} "
            + $"SET {sql.DelimitIdentifier("Version")} = {{0}} WHERE {sql.DelimitIdentifier("Id")} = {{1}}";

        await external.Database.ExecuteSqlRawAsync(update, [8, 2], cancellationToken);
    }

    /// <summary>Reads stored rows and both sides of the shared token independently of the tracked state.</summary>
    private static async Task<TableSharingRecoveryStore> ReadTableSharingRecoveryStoreAsync(
        TestDatabase database,
        string shape,
        CancellationToken cancellationToken
    )
    {
        await using var verification = await CreateTableSharingRecoveryContextAsync(database, shape,
            cancellationToken: cancellationToken);

        var nodes = await verification.Set<TableSharingRecoveryNode>().OrderBy(node => node.Id)
            .Select(node => new TableSharingRecoveryStoredNode(node.Id, node.TreeId, node.ParentId, node.Name,
                node.Left, node.Right, node.Depth, node.Position, node.Version, node.Payload))
            .ToArrayAsync(cancellationToken);

        TableSharingRecoveryStoredPayload[] payloads;

        if (shape == "owned")
        {
            payloads = await verification.Set<TableSharingRecoveryNode>().OrderBy(node => node.Id)
                .Select(node => new TableSharingRecoveryStoredPayload(node.Id, node.Owned!.Version, node.Owned.Value))
                .ToArrayAsync(cancellationToken);
        }
        else if (shape == "split")
        {
            payloads = await verification.Set<TableSharingRecoveryPeer>().OrderBy(peer => peer.Id)
                .Select(peer => new TableSharingRecoveryStoredPayload(peer.Id, peer.Version, peer.Value))
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            payloads = [];
        }

        var validation = await verification.NestedSet<TableSharingRecoveryNode>().InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

        return new TableSharingRecoveryStore(nodes, payloads, validation.TotalIssueCount);
    }

    /// <summary>Changes ordinary CLR payload without notifying EF or marking a property modified.</summary>
    private static void ChangeUnmarkedTableSharingPayload(TableSharingRecoveryGraph graph, string shape)
    {
        switch (shape)
        {
            case "owned":
                graph.Node.Owned!.Value = "raw unmarked payload";
                break;
            case "split":
                graph.Node.Peer!.Value = "raw unmarked payload";
                break;
            case "none":
                graph.Node.Payload = "raw unmarked payload";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    /// <summary>Reads caller payload independently of stored rows and original-value snapshots.</summary>
    private static string? CurrentTableSharingPayload(TableSharingRecoveryGraph graph, string shape)
        => shape switch
        {
            "owned" => graph.Node.Owned!.Value,
            "split" => graph.Node.Peer!.Value,
            "none" => graph.Node.Payload,
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

    /// <summary>Retains the actual principal and unchanged sharing entry used by the command batcher.</summary>
    private sealed record TableSharingRecoveryGraph(
        TableSharingRecoveryNode Node,
        EntityEntry<TableSharingRecoveryNode> Entry,
        EntityEntry? Peer);

    /// <summary>Copies all stored principal values without materializing entities into the tested tracker.</summary>
    private sealed record TableSharingRecoveryStoredNode(
        int Id, Guid TreeId, int? ParentId, string Name, long Left, long Right, int Depth, long Position,
        int Version, string Payload);

    /// <summary>Copies the shared token and ordinary owned or split payload.</summary>
    private sealed record TableSharingRecoveryStoredPayload(int Id, int Version, string? Value);

    /// <summary>Holds provider evidence independent of the failed context.</summary>
    private sealed record TableSharingRecoveryStore(
        TableSharingRecoveryStoredNode[] Nodes,
        TableSharingRecoveryStoredPayload[] Payloads,
        long Issues);
}
