using System.Collections;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Seeds deterministic JSON through EF with empty materialization-time collection initializers.</summary>
    private async Task<TestDatabase> CreateComplexCallbackDatabaseAsync()
    {
        var database = await TestDatabase.CreateAsync(Engine);

        try
        {
            await using var context = await CreateComplexCallbackContextAsync(database);
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(CancellationToken.None);
            var owner = new NavigationScopeOwner { Value = "owner" };
            var row = new ComplexCallbackRow
            {
                Id = 1,
                Items = [NewComplexCallbackItem("first"), NewComplexCallbackItem("second")],
                Address = new ComplexCallbackAddress { Text = "original address", Tags = NewComplexCallbackLeaves() },
            };

            await context.AddRangeAsync([owner, new NavigationScopeOwner { Value = "other owner" },
                new NavigationNote { Id = 1, Value = "sentinel" }, row], CancellationToken.None);

            await context.SaveChangesAsync(CancellationToken.None);
            var tree = context.NestedSet<NavigationNode>().ForScope(owner.Id);
            await tree.InsertRootAsync(new NavigationNode { Id = 1, Name = "Root" }, s_treeId,
                CancellationToken.None);

            await tree.InsertChildAsync(new NavigationNode { Id = 2, Name = "Alpha" }, 1, CancellationToken.None);
            await tree.InsertChildAsync(new NavigationNode { Id = 3, Name = "Bravo" }, 1, CancellationToken.None);

            return database;
        }
        catch
        {
            await database.DisposeAsync();

            throw;
        }
    }

    /// <summary>Creates the isolated ordinary-row model through the existing provider options.</summary>
    private static async Task<ComplexCallbackContext> CreateComplexCallbackContextAsync(
        TestDatabase database,
        params IInterceptor[] interceptors
    )
        => new(await SaveSemanticsFixture.OptionsAsync(database, interceptors));

    /// <summary>Supplies explicit seed values without causing EF materialization to append duplicates.</summary>
    private static ComplexCallbackItem NewComplexCallbackItem(string text)
        => new() { Text = text, Parts = NewComplexCallbackLeaves() };

    /// <summary>Provides two independent reference elements at every nested collection boundary.</summary>
    private static List<ComplexCallbackLeaf> NewComplexCallbackLeaves()
        => [new() { Text = "part one" }, new() { Text = "part two" }];

    /// <summary>Loads the ordinary row and sentinel without creating pending facade writes.</summary>
    private static async Task<ComplexCallbackGraph> LoadComplexCallbackGraphAsync(ComplexCallbackContext context)
    {
        var row = await context.Set<ComplexCallbackRow>().SingleAsync(CancellationToken.None);
        var sentinel = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        return new ComplexCallbackGraph(context.Entry(row), context.Entry(sentinel));
    }

    /// <summary>Authorizes one existing hierarchy order change independently of ordinary complex payload.</summary>
    private static async Task PrepareComplexCallbackOrderingAsync(ComplexCallbackContext context)
    {
        var node = await context.Set<NavigationNode>().SingleAsync(value => value.Id == 2, CancellationToken.None);
        node.Name = "Zulu";
        context.Entry(node).Property(value => value.Name).IsModified = true;
    }

    /// <summary>Mutates only raw complex CLR values at the requested callback boundary.</summary>
    private static void ChangeComplexCallbackRow(ComplexCallbackRow row, string mutation)
    {
        switch (mutation)
        {
            case "remove":
                row.Items!.RemoveAt(row.Items.Count - 1);
                break;
            case "nested-clear":
                row.Items![0].Parts!.Clear();
                break;
            case "list-null":
                row.Items = null;
                break;
            case "reference-null":
                row.Address = null;
                break;
            case "replace":
                row.Items = [NewComplexCallbackItem("replacement")];
                break;
            case "nested-null":
                row.Items![0].Parts = null;
                break;
            case "address-nested-clear":
                row.Address!.Tags!.Clear();
                break;
            case "edit":
                row.Items![0].Text = "callback item";
                break;
            case "add":
                row.Items!.Add(NewComplexCallbackItem("callback added"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    /// <summary>Copies direct caller identities and values without relying on live collection aliases.</summary>
    private static ComplexCallbackClrState CaptureComplexCallbackClr(ComplexCallbackRow row)
        => new(row, row.Items, row.Items?.Select(item => new ComplexCallbackItemState(item, item.Text,
                item.Parts, item.Parts?.Select(part => new ComplexCallbackLeafState(part, part.Text)).ToArray() ?? []))
            .ToArray() ?? [], row.Address, row.Address?.Text, row.Address?.Tags,
            row.Address?.Tags?.Select(tag => new ComplexCallbackLeafState(tag, tag.Text)).ToArray() ?? []);

    /// <summary>Captures public EF complex entries separately from caller-owned containers.</summary>
    private static ComplexCallbackNativeState[] CaptureComplexCallbackNative(ComplexCallbackGraph graph)
    {
        var states = new List<ComplexCallbackNativeState>();
        CaptureComplexCallbackCollection(graph.Row.ComplexCollection(value => value.Items), "Items", "Parts", states);
        var address = graph.Row.ComplexProperty(value => value.Address);
        states.Add(new ComplexCallbackNativeState("Address", address.CurrentValue, null, address.IsModified,
            CaptureComplexPeerLeaves(address.Properties), []));

        if (address.CurrentValue is not null)
        {
            CaptureComplexCallbackCollection(address.ComplexCollection("Tags"), "Address.Tags", null, states);
        }

        return states.ToArray();
    }

    /// <summary>Records fixture ordinals and originals without reading original-slot current values.</summary>
    private static void CaptureComplexCallbackCollection(
        ComplexCollectionEntry collection,
        string path,
        string? nestedName,
        List<ComplexCallbackNativeState> states
    )
    {
        var originals = Enumerable.Range(0, 2).SelectMany(index => collection.GetOriginalEntry(index).Properties
            .OrderBy(property => property.Metadata.Name, StringComparer.Ordinal)
            .Select(property => new ComplexPeerOriginalLeafState(
                index.ToString(CultureInfo.InvariantCulture) + ":" + property.Metadata.Name, property.OriginalValue)))
            .ToArray();

        states.Add(new ComplexCallbackNativeState(path, collection.CurrentValue, null, collection.IsModified,
            [], originals));

        if (collection.CurrentValue is not IList values)
        {
            return;
        }

        // WHY: Public EF complex entries are indexed by the caller's current ordinal, not by entity identity.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < values.Count; index++)
        {
            var entry = collection[index];
            var elementPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            states.Add(new ComplexCallbackNativeState(elementPath, values[index], entry.State, entry.IsModified,
                CaptureComplexPeerLeaves(entry.Properties), []));

            if (nestedName is not null)
            {
                CaptureComplexCallbackCollection(entry.ComplexCollection(nestedName), elementPath + "." + nestedName,
                    null, states);
            }
        }
    }

    /// <summary>Checks exact public complex state without treating record-array references as value equality.</summary>
    private static void AssertComplexCallbackNative(
        ComplexCallbackNativeState[] before,
        ComplexCallbackNativeState[] after
    )
    {
        Assert.Equal(before.Length, after.Length);

        // WHY: Capture order is stable; corresponding ordinals must retain identity, flags and original snapshots.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.Length; index++)
        {
            Assert.Equal(before[index].Path, after[index].Path);
            Assert.Same(before[index].Instance, after[index].Instance);
            Assert.Equal(before[index].State, after[index].State);
            Assert.Equal(before[index].Modified, after[index].Modified);
            Assert.Equal(before[index].Leaves, after[index].Leaves);
            Assert.Equal(before[index].Originals, after[index].Originals);
        }
    }

    /// <summary>Checks original containers, element identities and order together with copied raw values.</summary>
    private static void AssertComplexCallbackClr(ComplexCallbackClrState before, ComplexCallbackClrState after)
    {
        Assert.Same(before.Row, after.Row);
        Assert.Same(before.Items, after.Items);
        Assert.Same(before.Address, after.Address);
        Assert.Equal(before.AddressText, after.AddressText);
        Assert.Same(before.Tags, after.Tags);
        Assert.Equal(before.TagValues, after.TagValues);
        Assert.Equal(before.ItemValues.Length, after.ItemValues.Length);

        // WHY: Each caller-owned element and nested list must survive in its original current position.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < before.ItemValues.Length; index++)
        {
            var expected = before.ItemValues[index];
            var actual = after.ItemValues[index];
            Assert.Same(expected.Item, actual.Item);
            Assert.Equal(expected.Text, actual.Text);
            Assert.Same(expected.Parts, actual.Parts);
            Assert.Equal(expected.PartValues, actual.PartValues);
        }
    }

    /// <summary>Reads real stored payload independently of the tested tracker and its nullable CLR graph.</summary>
    private static async Task<ComplexCallbackStoredRow> ReadComplexCallbackRowAsync(TestDatabase database)
    {
        await using var verification = await CreateComplexCallbackContextAsync(database);
        var row = await verification.Set<ComplexCallbackRow>().AsNoTracking().SingleAsync(CancellationToken.None);

        return new ComplexCallbackStoredRow(row.Label, row.Version, row.Address?.Text,
            row.Items is null ? null : string.Join("|", row.Items.Select(item => item.Text + ":"
                + (item.Parts is null ? "null" : string.Join(",", item.Parts.Select(part => part.Text))))),
            row.Address?.Tags is null ? null : string.Join("|", row.Address.Tags.Select(tag => tag.Text)));
    }

    /// <summary>Advances only the independent application token to provoke genuine optimistic concurrency.</summary>
    private static async Task ChangeComplexCallbackVersionAsync(TestDatabase database)
    {
        await using var external = await CreateComplexCallbackContextAsync(database);
        var sql = external.GetService<ISqlGenerationHelper>();
        var update = $"UPDATE {sql.DelimitIdentifier("ComplexCallbackRows")} "
            + $"SET {sql.DelimitIdentifier("Version")} = {{0}} WHERE {sql.DelimitIdentifier("Id")} = {{1}}";

        await external.Database.ExecuteSqlRawAsync(update, [8, 1], CancellationToken.None);
    }

    /// <summary>Executes each managed insertion form without registering hierarchy inputs before preflight.</summary>
    private static Task InsertComplexCallbackInputsAsync(DbContext context, string shape, NavigationNode[] inputs)
    {
        var tree = context.NestedSet<NavigationNode>().ForScope(1);

        return shape switch
        {
            "single" => tree.InsertChildAsync(inputs[0], 1, CancellationToken.None),
            "subtree" => tree.InsertSubtreeAsync(PreparedOwnerBranch(inputs, 0, inputs.Length), 1,
                CancellationToken.None),
            "forest" => tree.InsertForestAsync(
                [new NestedSetTreeImport<NavigationNode, Guid>(s_manualFirstImportTree,
                        PreparedOwnerBranch(inputs, 0, inputs.Length / 2)),
                    new NestedSetTreeImport<NavigationNode, Guid>(s_manualSecondImportTree,
                        PreparedOwnerBranch(inputs, inputs.Length / 2, inputs.Length / 2))], CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    /// <summary>Retains exact application entries without incidental automatic detection.</summary>
    private sealed record ComplexCallbackGraph(
        EntityEntry<ComplexCallbackRow> Row, EntityEntry<NavigationNote> Sentinel);

    /// <summary>Retains caller object references and copied values before callback mutation.</summary>
    private sealed record ComplexCallbackClrState(
        ComplexCallbackRow Row, List<ComplexCallbackItem>? Items, ComplexCallbackItemState[] ItemValues,
        ComplexCallbackAddress? Address, string? AddressText, List<ComplexCallbackLeaf>? Tags,
        ComplexCallbackLeafState[] TagValues);

    /// <summary>Retains each element and its nested list without retaining mutable value aliases.</summary>
    private sealed record ComplexCallbackItemState(
        ComplexCallbackItem Item, string Text, List<ComplexCallbackLeaf>? Parts, ComplexCallbackLeafState[] PartValues);

    /// <summary>Copies a nested element's value while retaining its caller-owned object reference.</summary>
    private sealed record ComplexCallbackLeafState(ComplexCallbackLeaf Leaf, string Text);

    /// <summary>Copies actual EF scalar, original and modification state at a bounded fixture path.</summary>
    private sealed record ComplexCallbackNativeState(
        string Path, object? Instance, EntityState? State, bool Modified, ComplexPeerLeafState[] Leaves,
        ComplexPeerOriginalLeafState[] Originals);

    /// <summary>Copies every stored ordinary payload component relevant to recovery.</summary>
    private sealed record ComplexCallbackStoredRow(
        string Label, int Version, string? Address, string? Items, string? Tags);

    /// <summary>Mutates a captured ordinary callback write only after provider completion, then fails once.</summary>
    private sealed class ComplexCallbackCompletionFailure(Action mutation) : SaveChangesInterceptor
    {
        private bool _failed;

        /// <summary>Gets the exact original failure expected to survive complete rollback.</summary>
        internal InvalidOperationException Error { get; } = new("Injected complex callback completion failure.");

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!_failed)
            {
                _failed = true;
                mutation();

                throw Error;
            }

            return ValueTask.FromResult(result);
        }
    }
}
