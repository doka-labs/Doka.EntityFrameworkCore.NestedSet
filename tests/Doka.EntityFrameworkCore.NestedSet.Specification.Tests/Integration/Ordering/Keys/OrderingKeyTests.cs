namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies ordered mutations and saves with native string, Guid, and binary database identities.</summary>
public abstract partial class OrderingKeyTests : ProviderTest
{
    /// <summary>Owns the provider databases and isolated model tables shared by this test class.</summary>
    private readonly OrderingKeyFixture _fixture;

    /// <summary>Creates a test case using the fixture's isolated key-ordering model.</summary>
    /// <param name="fixture">The real relational database fixture.</param>
    protected OrderingKeyTests(
        IProviderFixture<OrderingKeyFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Arranges two roots, a movable subtree, tied siblings, and an independent scope.</summary>
    private async Task<KeyScenario<TKey, TParent>> CreateScenarioAsync<TKey, TParent>(
        string engine,
        Func<int, TKey> key,
        Func<TKey, TKey>? alias = null
    )
        where TKey : notnull
    {
        var context = await _fixture.ResetAsync(engine);

        try
        {
            var scenario = new KeyScenario<TKey, TParent>(context, key, alias);
            var tree = scenario.Tree;

            // WHY: Each root matches the other tree's filter, so a scope-wide query cannot pass as one exact tree.
            await tree.InsertRootAsync(scenario.Node(1, "Zulu"), Guid.NewGuid(), CancellationToken.None);
            await tree.InsertRootAsync(scenario.Node(9, "Same"), Guid.NewGuid(), CancellationToken.None);
            await tree.InsertChildAsync(scenario.Node(4, "Same"), scenario.Reference(1), CancellationToken.None);
            await tree.InsertChildAsync(scenario.Node(3, "Same"), scenario.Reference(1), CancellationToken.None);
            await tree.InsertChildAsync(scenario.Node(2, "Zulu"), scenario.Reference(1), CancellationToken.None);
            await tree.InsertChildAsync(scenario.Node(5, "Leaf"), scenario.Reference(2), CancellationToken.None);
            await tree.InsertChildAsync(scenario.Node(8, "Zulu"), scenario.Reference(9), CancellationToken.None);
            await scenario.Other.InsertRootAsync(
                scenario.Node(11, "Other root"),
                Guid.NewGuid(),
                CancellationToken.None);

            await scenario.Other.InsertChildAsync(
                scenario.Node(12, "Same"),
                scenario.Reference(11),
                CancellationToken.None);

            scenario.OtherBefore = await SnapshotAsync(scenario.OtherNodes);

            return scenario;
        }
        catch
        {
            // WHY: A failed arrangement never transfers ownership to the caller's await-using declaration.
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Checks sibling membership and positions against an independent native SQL order.</summary>
    private static async Task AssertChildrenAsync<TKey, TParent>(
        KeyScenario<TKey, TParent> scenario,
        int parent,
        IReadOnlyList<int> members
    )
        where TKey : notnull
    {
        var children = scenario.Tree.ChildrenOf(scenario.Reference(parent));
        var actual = await children
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var native = await children
            .OrderBy(node => node.Name)
            .ThenBy(node => node.Id)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // WHY: Membership is checked separately so two empty results cannot conceal a broken aliased parent lookup.
        Assert.Equal(
            members
                .Select(index => Identity(scenario.Key(index)))
                .Order(StringComparer.Ordinal),
            actual
                .Select(value => Identity(value))
                .Order(StringComparer.Ordinal));

        Assert.Equal(native.Select(value => Identity(value)), actual.Select(value => Identity(value)));
    }

    /// <summary>Checks that a whole-tree filter retains native sibling order and excludes other roots.</summary>
    private static async Task AssertFilteredTreeAsync<TKey, TParent>(
        KeyScenario<TKey, TParent> scenario,
        int parent,
        string name
    )
        where TKey : notnull
    {
        var native = await scenario
            .Tree
            .ChildrenOf(scenario.Reference(parent))
            .Where(node => node.Name == name)
            .OrderBy(node => node.Name)
            .ThenBy(node => node.Id)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var filtered = await scenario
            .Tree
            .TreeContaining(scenario.Reference(5))
            .Where(node => node.Name == name)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.NotEmpty(native);
        Assert.Equal(native.Select(value => Identity(value)), filtered.Select(value => Identity(value)));
    }

    /// <summary>Checks structural invariants and independent-scope integrity after the tested operation.</summary>
    private static async Task AssertIntegrityAsync<TKey, TParent>(
        KeyScenario<TKey, TParent> scenario
    )
        where TKey : notnull
    {
        var forest = await scenario.Nodes.ToArrayAsync(CancellationToken.None);

        Assert.NotEmpty(forest);

        // WHY: Aliased key equality belongs to the provider, while interval ancestry must remain tree-local.
        foreach (var group in forest.GroupBy(node => (node.Scope, node.TreeId)))
        {
            var nodes = group
                .OrderBy(node => node.Left)
                .ToArray();

            var root = Assert.Single(nodes, node => node.ParentId is null);
            Assert.Equal(0, root.Depth);
            Assert.Equal(0, root.Position);
            Assert.Equal(1, root.Left);
            Assert.Equal(nodes.Length * 2L, root.Right);
            Assert.Equal(
                Enumerable
                    .Range(1, nodes.Length * 2)
                    .Select(value => (long)value),
                nodes
                    .SelectMany(node => new[] { node.Left, node.Right })
                    .Order());

            foreach (var node in nodes)
            {
                var ancestors = nodes
                    .Where(parent => parent.Left < node.Left && parent.Right > node.Right)
                    .ToArray();

                var expectedParent = ancestors.Length == 0 ? null : Identity(ancestors[^1].Id);
                var parent = node.ParentId is { } value ? Identity(value) : null;
                Assert.Equal(expectedParent, parent);
                Assert.Equal(ancestors.Length, node.Depth);
                Assert.Equal(0, (node.Right - node.Left + 1) % 2);
            }

            foreach (var siblings in nodes.GroupBy(node => node.ParentId is { } parent ? Identity(parent) : null))
            {
                Assert.Equal(
                    Enumerable
                        .Range(0, siblings.Count())
                        .Select(value => (long)value),
                    siblings.Select(node => node.Position));
            }

            var report = await scenario
                .Tree
                .InTree(group.Key.TreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

            Assert.True(report.IsValid);
        }

        var otherTreeId = await scenario
            .OtherNodes
            .Select(node => node.TreeId)
            .Distinct()
            .SingleAsync(CancellationToken.None);

        var otherReport = await scenario
            .Other
            .InTree(otherTreeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        Assert.True(otherReport.IsValid);
        Assert.Equal(scenario.OtherBefore, await SnapshotAsync(scenario.OtherNodes));
    }

    /// <summary>Captures every persisted value with content-based keys for an unchanged-scope comparison.</summary>
    private static async Task<KeySnapshot[]> SnapshotAsync<TKey, TParent>(
        IQueryable<OrderingKeyNode<TKey, TParent>> query
    )
        where TKey : notnull
    {
        var rows = await query
            .OrderBy(node => node.Left)
            .ToArrayAsync(CancellationToken.None);

        return rows
            .Select(node => new KeySnapshot(
                Identity(node.Id),
                node.Scope,
                node.TreeId,
                node.ParentId is { } parent ? Identity(parent) : null,
                node.Name,
                node.Left,
                node.Right,
                node.Depth,
                node.Position))
            .ToArray();
    }

    /// <summary>Compares identity contents without imposing a CLR ordering contract on the database.</summary>
    private static string Identity(
        object key
    ) => key switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        Guid value => value.ToString("D"),
        string value => value.ToUpperInvariant(),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>Provides mixed-case string identities whose ordinal and case-insensitive order differ.</summary>
    private static string StringKey(
        int value
    ) => value switch
    {
        1 => "Root",
        2 => "Bravo-key",
        3 => "alpha-key",
        4 => "Zulu-key",
        5 => "Nested-leaf",
        6 => "charlie-key",
        8 => "Echo-key",
        9 => "Destination",
        11 => "Other-root",
        12 => "Other-child",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Provides deterministic Guid identities whose physical representation remains provider-owned.</summary>
    private static Guid GuidKey(
        int value
    ) => Guid.Parse(
        value.ToString("X8", CultureInfo.InvariantCulture) + "-2345-6789-abcd-010203040506");

    /// <summary>Returns a fresh array for each reference so identity lookup must use binary contents.</summary>
    private static byte[] BinaryKey(
        int value
    ) => [0, (byte)value, 0xFF];

    /// <summary>Owns one arranged context, public facades, and identity aliases for a single behavioral case.</summary>
    private sealed class KeyScenario<TKey, TParent> : IAsyncDisposable
        where TKey : notnull
    {
        /// <summary>The optional spelling transformation used to probe database key equality.</summary>
        private readonly Func<TKey, TKey>? _alias;

        /// <summary>Creates public facades selecting two independent scopes in the same entity table.</summary>
        internal KeyScenario(
            OrderingKeyContext context,
            Func<int, TKey> key,
            Func<TKey, TKey>? alias
        )
        {
            Context = context;
            Key = key;
            _alias = alias;
            Tree = context
                .NestedSet<OrderingKeyNode<TKey, TParent>>()
                .ForScope(1);
            Other = context
                .NestedSet<OrderingKeyNode<TKey, TParent>>()
                .ForScope(2);
        }

        /// <summary>Gets the context used for normal tracked payload saves.</summary>
        internal OrderingKeyContext Context { get; }

        /// <summary>Gets the native key factory.</summary>
        internal Func<int, TKey> Key { get; }

        /// <summary>Gets the scope changed by the operation under test.</summary>
        internal ScopedNestedSet<OrderingKeyNode<TKey, TParent>, int> Tree { get; }

        /// <summary>Gets the independent scope used to detect unintended range updates.</summary>
        internal ScopedNestedSet<OrderingKeyNode<TKey, TParent>, int> Other { get; }

        /// <summary>Reads the scoped payload set for tracking and assertions across its independent trees.</summary>
        internal IQueryable<OrderingKeyNode<TKey, TParent>> Nodes =>
            Context
                .Set<OrderingKeyNode<TKey, TParent>>()
                .AsNoTracking()
                .Where(node => node.Scope == 1);

        /// <summary>Reads the independent scope whose payload and tree identity must remain unchanged.</summary>
        internal IQueryable<OrderingKeyNode<TKey, TParent>> OtherNodes =>
            Context
                .Set<OrderingKeyNode<TKey, TParent>>()
                .AsNoTracking()
                .Where(node => node.Scope == 2);

        /// <summary>Gets or sets the complete independent-scope state captured during arrangement.</summary>
        internal KeySnapshot[] OtherBefore { get; set; } = [];

        /// <summary>Creates an uninitialized node with the selected native identity and domain value.</summary>
        internal OrderingKeyNode<TKey, TParent> Node(
            int value,
            string name
        ) => new(Key(value), name);

        /// <summary>Returns an equivalent identity with a fresh array or alternate string casing.</summary>
        internal TKey Reference(
            int value
        )
        {
            var key = Key(value);

            return _alias is null ? key : _alias(key);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    /// <summary>Captures payload and structure without relying on array-reference equality.</summary>
    /// <param name="Id">The content-normalized identity.</param>
    /// <param name="Scope">The isolated scope.</param>
    /// <param name="TreeId">The independent coordinate identity.</param>
    /// <param name="Parent">The content-normalized nullable parent identity.</param>
    /// <param name="Name">The domain order value.</param>
    /// <param name="Left">The persisted left boundary.</param>
    /// <param name="Right">The persisted right boundary.</param>
    /// <param name="Depth">The persisted depth.</param>
    /// <param name="Position">The persisted sibling position.</param>
    private sealed record KeySnapshot(
        string Id,
        int Scope,
        Guid TreeId,
        string? Parent,
        string Name,
        long Left,
        long Right,
        int Depth,
        long Position
    );
}
