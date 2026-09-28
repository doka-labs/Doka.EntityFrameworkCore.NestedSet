namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingKeyTests
{
    /// <summary>
    /// Gets sorted string and binary import cases under application collection-translation overrides.
    /// </summary>
    public static TheoryData<ParameterTranslationMode, string> BulkCollectionModeCases
    {
        get
        {
            var cases = new TheoryData<ParameterTranslationMode, string>();

            foreach (var mode in new[] { ParameterTranslationMode.Constant, ParameterTranslationMode.Parameter })
            {
                cases.Add(mode, "String");
                cases.Add(mode, "Binary");
            }

            return cases;
        }
    }

    /// <summary>Sorted imports refresh every imported key without adopting the application's collection mode.</summary>
    /// <param name="mode">The application's collection-translation preference.</param>
    /// <param name="key">The native mapped key representation.</param>
    [Theory]
    [MemberData(nameof(BulkCollectionModeCases))]
    public Task SortedBulkRefreshRetainsParameterizedNativeKeys(
        ParameterTranslationMode mode,
        string key
    ) => key switch
    {
        "String" => RefreshCollectionModeBulkAsync<string, string?>(
            Engine,
            mode,
            CollectionStringKey,
            static value => value.ToUpperInvariant()),
        "Binary" => RefreshCollectionModeBulkAsync<byte[], byte[]?>(Engine, mode, BinaryKey),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>
    /// A late input identity edit rolls back sorted imports under both application collection overrides.
    /// </summary>
    /// <param name="mode">The application's collection-translation preference.</param>
    /// <param name="key">The native mapped key representation.</param>
    [Theory]
    [MemberData(nameof(BulkCollectionModeCases))]
    public Task SortedBulkRefreshIdentityEditRestoresInputsAndPersistedTrees(
        ParameterTranslationMode mode,
        string key
    ) => key switch
    {
        "String" => RejectCollectionModeIdentityEditAsync<string, string?>(
            Engine,
            mode,
            CollectionStringKey,
            static node => node.Id = "Edited-import-key",
            static value => value.ToUpperInvariant()),
        "Binary" => RejectCollectionModeIdentityEditAsync<byte[], byte[]?>(
            Engine,
            mode,
            BinaryKey,
            static node => node.Id[1] = 0xFE),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>
    /// Uses the actual ordered mapping and compares final CLR values with independent persisted snapshots.
    /// </summary>
    /// <typeparam name="TKey">The actual string or binary principal key.</typeparam>
    /// <typeparam name="TParent">The matching nullable parent representation.</typeparam>
    /// <param name="engine">The fixture-owned relational engine.</param>
    /// <param name="mode">The application's collection-translation override.</param>
    /// <param name="key">Creates independent native key values.</param>
    /// <param name="alias">An optional provider-equivalent parent reference.</param>
    private async Task RefreshCollectionModeBulkAsync<TKey, TParent>(
        string engine,
        ParameterTranslationMode mode,
        Func<int, TKey> key,
        Func<TKey, TKey>? alias = null
    )
        where TKey : notnull
    {
        // Arrange
        await using var arranged = await CreateScenarioAsync<TKey, TParent>(engine, key, alias);
        var otherRootBefore = await SnapshotAsync(arranged.Tree.TreeContaining(arranged.Reference(9)));
        var probe = new EnterpriseProbe();
        var providerOptions = ModelCompatibilityDatabase.Options<OrderingKeyContext>(
            engine,
            arranged.Context.Database.GetConnectionString()!,
            mode);

        var options = new DbContextOptionsBuilder<OrderingKeyContext>(providerOptions).AddInterceptors(probe).Options;

        await using var scenario = new KeyScenario<TKey, TParent>(new OrderingKeyContext(options), key, alias)
        {
            OtherBefore = arranged.OtherBefore,
        };

        var importedRoot = scenario.Node(6, "Same");
        var later = scenario.Node(20, "Zulu");
        var earlier = scenario.Node(21, "Alpha");
        var branch = new NestedSetBranch<OrderingKeyNode<TKey, TParent>>(
            importedRoot,
            [
                new NestedSetBranch<OrderingKeyNode<TKey, TParent>>(later),
                new NestedSetBranch<OrderingKeyNode<TKey, TParent>>(earlier),
            ]);

        OrderingKeyNode<TKey, TParent>[] imported = [importedRoot, later, earlier];

        // Act
        await scenario.Tree.InsertSubtreeAsync(branch, scenario.Reference(1), CancellationToken.None);

        // Assert
        await AssertChildrenAsync(scenario, 1, [2, 3, 4, 6]);
        await AssertChildrenAsync(scenario, 6, [20, 21]);
        await AssertIntegrityAsync(scenario);
        Assert.Equal(otherRootBefore, await SnapshotAsync(scenario.Tree.TreeContaining(scenario.Reference(9))));
        var persisted = await SnapshotAsync(scenario.Tree.TreeContaining(scenario.Reference(1)));

        Assert.All(
            imported,
            node =>
            {
                var actual = Assert.Single(persisted, row => row.Id == Identity(node.Id));
                Assert.Equal(
                    (actual.Scope, actual.TreeId, actual.Parent, actual.Left, actual.Right, actual.Depth,
                        actual.Position),
                    (node.Scope, node.TreeId, node.ParentId is { } parent ? Identity(parent) : null, node.Left,
                        node.Right, node.Depth, node.Position));
                Assert.Equal(EntityState.Detached, scenario.Context.Entry(node).State);
            });

        var refreshIndexes = probe
            .Commands
            .Select((
                command,
                index
            ) => (command, index))
            .Where(item => item.command.Contains(NestedSetDiagnostics.BulkRefreshTag, StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();

        var refreshIndex = Assert.Single(refreshIndexes);
        var parameters = probe.ParameterValues[refreshIndex];

        // WHY: Existing sibling subtrees can split the import interval after native ordering. This refresh must
        // select exactly the imported keys with scalar bindings regardless of application collection settings.
        Assert.All(
            imported,
            node => Assert.Contains(
                parameters,
                value => value is string or byte[] && Identity(value) == Identity(node.Id)));
    }

    /// <summary>
    /// Edits an input only after its sorted import writes complete and its key snapshots are captured.
    /// </summary>
    /// <typeparam name="TKey">The native string or binary identity.</typeparam>
    /// <typeparam name="TParent">The matching nullable parent representation.</typeparam>
    /// <param name="engine">The fixture-owned relational engine.</param>
    /// <param name="mode">The application's collection-translation override.</param>
    /// <param name="key">Creates independent native identities.</param>
    /// <param name="edit">Replaces the string identity or mutates the binary identity in place.</param>
    /// <param name="alias">An optional provider-equivalent destination reference.</param>
    private async Task RejectCollectionModeIdentityEditAsync<TKey, TParent>(
        string engine,
        ParameterTranslationMode mode,
        Func<int, TKey> key,
        Action<OrderingKeyNode<TKey, TParent>> edit,
        Func<TKey, TKey>? alias = null
    )
        where TKey : notnull
    {
        // Arrange
        await using var arranged = await CreateScenarioAsync<TKey, TParent>(engine, key, alias);
        var before = await SnapshotAsync(arranged.Nodes);
        var table = arranged.Context.Model.FindEntityType(typeof(OrderingKeyNode<TKey, TParent>))!.GetTableName()!;
        var probe = new BulkIntervalRefreshProbe(table);
        var providerOptions = ModelCompatibilityDatabase.Options<OrderingKeyContext>(
            engine,
            arranged.Context.Database.GetConnectionString()!,
            mode);

        var options = new DbContextOptionsBuilder<OrderingKeyContext>(providerOptions).AddInterceptors(probe).Options;

        await using var scenario = new KeyScenario<TKey, TParent>(new OrderingKeyContext(options), key, alias)
        {
            OtherBefore = arranged.OtherBefore,
        };

        var root = scenario.Node(6, "Same");
        root.Scope = 17;
        root.Left = 71;
        root.Right = 72;
        var child = new OrderingKeyNode<TKey, TParent>(key(20), "Alpha");
        var later = new OrderingKeyNode<TKey, TParent>(key(21), "Zulu");
        OrderingKeyNode<TKey, TParent>[] imported = [root, child, later];
        var original = imported
            .Select(node => new KeySnapshot(
                ExactCollectionIdentity(node.Id),
                node.Scope,
                node.TreeId,
                node.ParentId is { } parent ? ExactCollectionIdentity(parent) : null,
                node.Name,
                node.Left,
                node.Right,
                node.Depth,
                node.Position))
            .ToArray();

        var branch = new NestedSetBranch<OrderingKeyNode<TKey, TParent>>(
            root,
            [
                new NestedSetBranch<OrderingKeyNode<TKey, TParent>>(later),
                new NestedSetBranch<OrderingKeyNode<TKey, TParent>>(child),
            ]);

        scenario.Context.SavedChanges += (_, _) => probe.Inserted = true;

        // WHY: This reader boundary follows the INSERT and ordering writes. Editing only the caller's identity
        // leaves persisted rows intact and proves that a late verification failure restores the whole operation.
        probe.BeforeRefresh = (_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            edit(root);

            return Task.CompletedTask;
        };

        // Act
        var error = await Record.ExceptionAsync(() => scenario.Tree.InsertSubtreeAsync(
            branch,
            scenario.Reference(1),
            CancellationToken.None));

        probe.Inserted = false;

        // Assert
        Assert.IsType<DbUpdateConcurrencyException>(error);
        Assert.Single(probe.Commands);
        Assert.Equal(before, await SnapshotAsync(scenario.Nodes));
        Assert.Equal(
            original,
            imported.Select(node => new KeySnapshot(
                ExactCollectionIdentity(node.Id),
                node.Scope,
                node.TreeId,
                node.ParentId is { } parent ? ExactCollectionIdentity(parent) : null,
                node.Name,
                node.Left,
                node.Right,
                node.Depth,
                node.Position)));

        Assert.Empty(scenario.Context.ChangeTracker.Entries());
        await AssertIntegrityAsync(scenario);
    }

    /// <summary>Preserves original casing as well as binary contents when checking rollback representations.</summary>
    /// <param name="value">The original or restored native identity.</param>
    /// <returns>The exact string value or binary bytes.</returns>
    private static string ExactCollectionIdentity(
        object value
    ) => value is byte[] bytes ? Convert.ToHexString(bytes) : (string)value;

    /// <summary>Extends the arranged string key set with two distinct imported child identities.</summary>
    /// <param name="index">The arranged or imported node number.</param>
    /// <returns>A deterministic principal key using the actual case-insensitive mapping.</returns>
    private static string CollectionStringKey(
        int index
    ) => index >= 20
        ? "Imported-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
        : StringKey(index);
}
