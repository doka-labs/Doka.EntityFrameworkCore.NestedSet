namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Seeds complex values through EF rather than provider-specific JSON SQL.</summary>
    private async Task<TestDatabase> CreateComplexPeerRecoveryDatabaseAsync(string shape)
    {
        var database = await TestDatabase.CreateAsync(Engine);

        try
        {
            await using var context = await CreateComplexPeerRecoveryContextAsync(database, shape);
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(CancellationToken.None);
            var tree = context.NestedSet<ComplexPeerRecoveryNode>();
            await tree.InsertRootAsync(NewComplexPeerNode(shape, 1, "Root"), s_treeId, CancellationToken.None);
            await tree.InsertChildAsync(NewComplexPeerNode(shape, 2, "Alpha"), 1, CancellationToken.None);
            await tree.InsertChildAsync(NewComplexPeerNode(shape, 3, "Bravo"), 1, CancellationToken.None);

            if (shape != "owned")
            {
                // WHY: Ordinary peers are not facade inputs. EF serializes their complex values when
                // they are added to their already persisted, tracked principal rows.
                var nodes = await context.Set<ComplexPeerRecoveryNode>().ToArrayAsync(CancellationToken.None);

                foreach (var node in nodes)
                {
                    var payload = NewComplexPeerPayload(node.Id);
                    payload.Node = node;
                    await context.AddAsync(payload, CancellationToken.None);
                }

                await context.SaveChangesAsync(CancellationToken.None);
            }

            // WHY: EF appends materialized collection items to a constructor-created list. Seed values belong
            // in the factory, not the entity initializer; verify the physical JSON contains exactly two items.
            var stored = await ReadComplexPeerStoreAsync(database, shape);
            Assert.All(stored.Rows, row => Assert.Equal("first|second", row.Items));

            return database;
        }
        catch
        {
            await database.DisposeAsync();

            throw;
        }
    }

    /// <summary>Creates only ownership graphs accepted by facade insertion.</summary>
    private static ComplexPeerRecoveryNode NewComplexPeerNode(string shape, int id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Details = shape == "owned" ? NewComplexPeerPayload(id) : null,
        };

    /// <summary>Supplies deterministic seed items without prepopulating lists used by EF materialization.</summary>
    private static ComplexPeerPayload NewComplexPeerPayload(int id)
        => new()
        {
            Id = id,
            Items = [new ComplexPeerItem { Text = "first" }, new ComplexPeerItem { Text = "second" }],
        };

    /// <summary>Reuses provider registration with separate model cache entries for each storage shape.</summary>
    private static async Task<ComplexPeerRecoveryContext> CreateComplexPeerRecoveryContextAsync(
        TestDatabase database,
        string shape,
        params IInterceptor[] interceptors
    )
    {
        var options = new DbContextOptionsBuilder(await SaveSemanticsFixture.OptionsAsync(database, interceptors))
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>().Options;

        return new ComplexPeerRecoveryContext(options, shape);
    }

    /// <summary>Marks only the order property while retaining a genuinely unchanged complex peer.</summary>
    private static async Task<ComplexPeerGraph> PrepareComplexPeerRecoveryAsync(ComplexPeerRecoveryContext context)
    {
        var node = await context.Set<ComplexPeerRecoveryNode>().Include(value => value.Details)
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var entry = context.Entry(node);
        node.Name = "Zulu";
        entry.Property(value => value.Name).IsModified = true;
        var payload = node.Details ?? throw new InvalidOperationException("The complex peer is missing.");

        return new ComplexPeerGraph(node, entry, context.Entry(payload));
    }

    /// <summary>Changes CLR values without notifying EF or authorizing a payload UPDATE.</summary>
    private static void ChangeComplexPeerPayload(ComplexPeerGraph graph, string mutation)
    {
        var payload = graph.Peer.Entity;

        switch (mutation)
        {
            case "edit":
                payload.Items[0].Text = "raw item";
                break;
            case "add":
                payload.Items.Add(new ComplexPeerItem { Text = "raw added" });
                break;
            case "remove":
                payload.Items.RemoveAt(0);
                break;
            case "replace":
                payload.Items = [new ComplexPeerItem { Text = "raw replacement" }];
                break;
            case "null":
                payload.Address = null;
                break;
            case "address-edit":
                payload.Address!.Text = "raw address";
                break;
            case "address-replace":
                payload.Address = new ComplexPeerAddress { Text = "raw replacement address" };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    /// <summary>Captures public EF complex entries and caller container and element identities.</summary>
    private static ComplexPeerState CaptureComplexPeerState(ComplexPeerGraph graph)
    {
        var payload = graph.Peer.Entity;
        var address = graph.Peer.ComplexProperty(value => value.Address);
        var collection = graph.Peer.ComplexCollection(value => value.Items);
        var elements = payload.Items.Select((item, index) =>
        {
            var entry = collection[index];

            return new ComplexPeerElementState(item, entry.State, entry.IsModified,
                CaptureComplexPeerLeaves(entry.Properties));
        }).ToArray();

        // WHY: This fixture stores exactly two original elements. Read their actual EF original entries,
        // independently of raw caller additions, removals or replacement of the current collection.
        var originals = Enumerable.Range(0, 2).Select(index =>
            collection.GetOriginalEntry(index).Properties.OrderBy(property => property.Metadata.Name,
                    StringComparer.Ordinal)
                .Select(property => new ComplexPeerOriginalLeafState(property.Metadata.Name, property.OriginalValue))
                .ToArray()).ToArray();

        return new ComplexPeerState(payload, payload.Address, payload.Address?.Text, payload.Address?.Extra,
            address.IsModified, CaptureComplexPeerLeaves(address.Properties), payload.Items, collection.IsModified,
            elements, originals);
    }

    /// <summary>Copies scalar values and modification flags without requesting change detection.</summary>
    private static ComplexPeerLeafState[] CaptureComplexPeerLeaves(IEnumerable<PropertyEntry> properties)
        => properties.OrderBy(property => property.Metadata.Name, StringComparer.Ordinal).Select(property =>
            new ComplexPeerLeafState(property.Metadata.Name, property.CurrentValue, property.OriginalValue,
                property.IsModified)).ToArray();

    /// <summary>Checks exact identities, leaves, collection state and pending caller order.</summary>
    private static void AssertComplexPeerState(ComplexPeerState before, ComplexPeerState after)
    {
        Assert.Same(before.Payload, after.Payload);
        Assert.Same(before.Address, after.Address);
        Assert.Equal(before.AddressText, after.AddressText);
        Assert.Equal(before.AddressExtra, after.AddressExtra);
        Assert.Equal(before.AddressModified, after.AddressModified);
        Assert.Equal(before.AddressLeaves, after.AddressLeaves);
        Assert.Same(before.Items, after.Items);
        Assert.Equal(before.CollectionModified, after.CollectionModified);
        Assert.Equal(before.Elements.Length, after.Elements.Length);

        // WHY: Compare matching positions explicitly because element identity and caller order are separate contracts.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Elements.Length; index++)
        {
            Assert.Same(before.Elements[index].Item, after.Elements[index].Item);
            Assert.Equal(before.Elements[index].State, after.Elements[index].State);
            Assert.Equal(before.Elements[index].Modified, after.Elements[index].Modified);
            Assert.Equal(before.Elements[index].Leaves, after.Elements[index].Leaves);
        }

        // WHY: Preserve the installed original ordinal independently of the caller's current list shape.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Originals.Length; index++)
        {
            Assert.Equal(before.Originals[index], after.Originals[index]);
        }
    }

    /// <summary>Checks caller values after acceptance without adding original-snapshot guarantees.</summary>
    private static void AssertComplexPeerCallerValues(ComplexPeerState before, ComplexPeerGraph graph)
    {
        var payload = graph.Peer.Entity;
        Assert.Same(before.Payload, payload);
        Assert.Same(before.Address, payload.Address);
        Assert.Equal(before.AddressText, payload.Address?.Text);
        Assert.Equal(before.AddressExtra, payload.Address?.Extra);
        Assert.Same(before.Items, payload.Items);
        Assert.Equal(before.Elements.Length, payload.Items.Count);

        // WHY: The caller's raw list order and individual element identities remain independent of EF acceptance.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Elements.Length; index++)
        {
            Assert.Same(before.Elements[index].Item, payload.Items[index]);
            Assert.Equal(before.Elements[index].Leaves.Single(leaf => leaf.Name == "Text").Current,
                payload.Items[index].Text);
        }
    }

    /// <summary>Reads complex payload and a full tree validation in an independent context.</summary>
    private static async Task<ComplexPeerStore> ReadComplexPeerStoreAsync(TestDatabase database, string shape)
    {
        await using var verification = await CreateComplexPeerRecoveryContextAsync(database, shape);
        var nodes = await verification.Set<ComplexPeerRecoveryNode>().AsNoTracking().Include(node => node.Details)
            .OrderBy(node => node.Id).ToArrayAsync(CancellationToken.None);

        var rows = nodes.Select(node => new ComplexPeerStoredRow(node.Id, node.Name, node.TreeId, node.ParentId,
            node.Left, node.Right, node.Depth, node.Position, node.Details!.Address?.Text,
            string.Join("|", node.Details.Items.Select(item => item.Text)))).ToArray();

        var validation = await verification.NestedSet<ComplexPeerRecoveryNode>().InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        return new ComplexPeerStore(rows, validation.TotalIssueCount);
    }

    /// <summary>Retains the principal and actual unchanged owned or split peer.</summary>
    private sealed record ComplexPeerGraph(
        ComplexPeerRecoveryNode Node,
        EntityEntry<ComplexPeerRecoveryNode> Entry,
        EntityEntry<ComplexPeerPayload> Peer);

    /// <summary>Copies a complex scalar's current, original and modification state.</summary>
    private sealed record ComplexPeerLeafState(string Name, object? Current, object? Original, bool Modified);

    /// <summary>Copies installed original values without indexing the caller's possibly shorter current list.</summary>
    private sealed record ComplexPeerOriginalLeafState(string Name, object? Original);

    /// <summary>Retains actual element identity alongside its public EF entry state.</summary>
    private sealed record ComplexPeerElementState(
        ComplexPeerItem Item, EntityState State, bool Modified, ComplexPeerLeafState[] Leaves);

    /// <summary>Retains all caller-owned complex identities and their exact native tracker state.</summary>
    private sealed record ComplexPeerState(
        ComplexPeerPayload Payload, ComplexPeerAddress? Address, string? AddressText, string? AddressExtra,
        bool AddressModified, ComplexPeerLeafState[] AddressLeaves, List<ComplexPeerItem> Items,
        bool CollectionModified, ComplexPeerElementState[] Elements, ComplexPeerOriginalLeafState[][] Originals);

    /// <summary>Copies one stored hierarchy row and its independent complex payload.</summary>
    private sealed record ComplexPeerStoredRow(
        int Id, string Name, Guid TreeId, int? ParentId, long Left, long Right, int Depth, long Position,
        string? Address, string Items);

    /// <summary>Holds actual persisted rows and full hierarchy validation evidence.</summary>
    private sealed record ComplexPeerStore(ComplexPeerStoredRow[] Rows, long Issues);
}
