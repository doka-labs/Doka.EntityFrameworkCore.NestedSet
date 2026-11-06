namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Seeds a shared application row and a valid hierarchy in an isolated database.</summary>
    private async Task<TestDatabase> CreateManagedSharingDatabaseAsync(
        string shape,
        CancellationToken cancellationToken
    )
    {
        var database = await TestDatabase.CreateAsync(Engine, cancellationToken: cancellationToken);

        try
        {
            await using var context = await CreateManagedSharingContextAsync(
                database,
                shape,
                cancellationToken: cancellationToken);

            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);

            var owner = new NavigationScopeOwner { Value = "owner" };
            var row = new ManagedSharingRow { Id = 1 };
            row.Payload = new ManagedSharingPayload
            {
                Id = row.Id,
                Row = row,
            };

            await context.AddRangeAsync(
                [
                    owner, new NavigationScopeOwner { Value = "other owner" },
                    new NavigationNote
                    {
                        Id = 1,
                        Value = "sentinel",
                    },
                    row,
                ],
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
            var tree = context
                .NestedSet<NavigationNode>()
                .ForScope(owner.Id);

            await tree.InsertRootAsync(
                new NavigationNode
                {
                    Id = 1,
                    Name = "Root",
                },
                s_treeId,
                cancellationToken);

            await tree.InsertChildAsync(
                new NavigationNode
                {
                    Id = 2,
                    Name = "Alpha",
                },
                1,
                cancellationToken);

            await tree.InsertChildAsync(
                new NavigationNode
                {
                    Id = 3,
                    Name = "Bravo",
                },
                1,
                cancellationToken);

            return database;
        }
        catch
        {
            // WHY: The test owns this database only after both models and their seed rows exist.
            await database.DisposeAsync();

            throw;
        }
    }

    /// <summary>Reuses provider setup while separating the owned and ordinary sharing models.</summary>
    private static async Task<ManagedSharingContext> CreateManagedSharingContextAsync(
        TestDatabase database,
        string shape,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    )
    {
        var options = new DbContextOptionsBuilder(
                await SaveSemanticsFixture.OptionsAsync(
                    database,
                    cancellationToken: cancellationToken,
                    interceptors))
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .Options;

        return new ManagedSharingContext(options, shape);
    }

    /// <summary>Retains application entries and the sentinel before the facade's clean-context preflight.</summary>
    private static async Task<ManagedSharingGraph> PrepareManagedSharingGraphAsync(
        ManagedSharingContext context,
        CancellationToken cancellationToken
    )
    {
        var row = await context
            .Set<ManagedSharingRow>()
            .Include(value => value.Payload)
            .SingleAsync(cancellationToken);

        var sentinel = await context
            .Set<NavigationNote>()
            .SingleAsync(cancellationToken);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var payload = row.Payload ?? throw new InvalidOperationException("The shared application payload is missing.");

        return new ManagedSharingGraph(context.Entry(row), context.Entry(payload), context.Entry(sentinel));
    }

    /// <summary>Expresses only explicit callback writes while leaving the counterpart's raw payload unmarked.</summary>
    private static void StageManagedSharingCallback(
        ManagedSharingGraph graph,
        bool reverse
    )
    {
        if (reverse)
        {
            graph.Row.Entity.Text = "raw pending row";
            graph.Payload.Property(value => value.Value)
                .CurrentValue = "callback payload";
        }
        else
        {
            graph.Payload.Entity.Value = "raw pending payload";
            graph.Row.Property(value => value.Text)
                .CurrentValue = "callback row";
        }
    }

    /// <summary>Captures only application entries; active insertion inputs have a separate cleanup owner.</summary>
    private static PlannedEntryState[] CaptureManagedSharingState(
        DbContext context,
        ManagedSharingGraph graph
    )
        => CapturePlannedParentState(context)
            .Where(entry => ReferenceEquals(entry.Entity, graph.Row.Entity)
                || ReferenceEquals(entry.Entity, graph.Payload.Entity)
                || ReferenceEquals(entry.Entity, graph.Sentinel.Entity))
            .ToArray();

    /// <summary>Executes single or bounded bulk insertion without adding hierarchy rows before authorization.</summary>
    private static Task InsertManagedSharingShapeAsync(
        DbContext context,
        NavigationNode[] inputs,
        bool bulk,
        CancellationToken cancellationToken
    )
    {
        var tree = context
            .NestedSet<NavigationNode>()
            .ForScope(1);

        return bulk
            ? tree.InsertSubtreeAsync(
                new NestedSetBranch<NavigationNode>(
                    inputs[0],
                    [new NestedSetBranch<NavigationNode>(inputs[1])]),
                1,
                cancellationToken)
            : tree.InsertChildAsync(inputs[0], 1, cancellationToken);
    }

    /// <summary>Changes only the stored app token so the callback encounters actual optimistic concurrency.</summary>
    private static async Task ChangeManagedSharingVersionAsync(
        TestDatabase database,
        string shape,
        CancellationToken cancellationToken
    )
    {
        await using var external = await CreateManagedSharingContextAsync(
            database,
            shape,
            cancellationToken: cancellationToken);

        var sql = external.GetService<ISqlGenerationHelper>();
        var update = $"UPDATE {sql.DelimitIdentifier("ManagedSharingRows")} "
            + $"SET {sql.DelimitIdentifier("Version")} = {{0}} WHERE {sql.DelimitIdentifier("Id")} = {{1}}";

        await external.Database.ExecuteSqlRawAsync(update, [8, 1], cancellationToken);
    }

    /// <summary>Reads actual application payload and both sides of the shared concurrency token.</summary>
    private static async Task<ManagedSharingStoredRow> ReadManagedSharingRowAsync(
        TestDatabase database,
        string shape,
        CancellationToken cancellationToken
    )
    {
        await using var verification = await CreateManagedSharingContextAsync(
            database,
            shape,
            cancellationToken: cancellationToken);

        return await verification
            .Set<ManagedSharingRow>()
            .Select(row => new ManagedSharingStoredRow(row.Text, row.Version, row.Payload!.Value, row.Payload.Version))
            .SingleAsync(cancellationToken);
    }

    /// <summary>Holds application entries without incidental detection in callbacks or assertions.</summary>
    private sealed record ManagedSharingGraph(
        EntityEntry<ManagedSharingRow> Row,
        EntityEntry<ManagedSharingPayload> Payload,
        EntityEntry<NavigationNote> Sentinel
    );

    /// <summary>Copies persisted shared-row payload independently of the failed tracker.</summary>
    private sealed record ManagedSharingStoredRow(
        string Text,
        int Version,
        string Payload,
        int PayloadVersion
    );
}
