namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks persisted sibling order and ordinary saves against all supported relational engines.</summary>
public abstract partial class OrderingTests : ProviderTest
{
    private readonly OrderingFixture _fixture;

    /// <summary>Creates a case using independent ordering tables in a fixture-owned real database.</summary>
    protected OrderingTests(
        IProviderFixture<OrderingFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Creates a detached domain node whose coordinates will be assigned by the hierarchy operation.</summary>
    private static OrderingNode Node(
        int id,
        string? name,
        int priority = 0
    ) => new()
    {
        Id = id,
        Name = name,
        Priority = priority
    };

    /// <summary>Selects the public hierarchy facade for one application partition.</summary>
    private static ScopedNestedSet<OrderingNode, int> Tree(
        OrderingContext context,
        int scope = 1
    ) => context
        .NestedSet<OrderingNode>()
        .ForScope(scope);

    /// <summary>Reads the scoped payload set when an assertion deliberately spans independent trees.</summary>
    private static IQueryable<OrderingNode> Nodes(
        OrderingContext context,
        int scope = 1
    ) => context
        .Set<OrderingNode>()
        .AsNoTracking()
        .Where(node => node.Scope == scope);

    /// <summary>Materializes only identities while retaining a query's database order.</summary>
    private static Task<int[]> IdsAsync(
        IQueryable<OrderingNode> query
    ) => query
        .Select(node => node.Id)
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Checks persisted identity order after the asynchronous query has finished.</summary>
    private static async Task AssertIdsAsync(
        IQueryable<OrderingNode> query,
        params int[] expected
    )
    {
        var actual = await IdsAsync(query);

        Assert.Equal(expected, actual);
    }

    /// <summary>Seeds three alphabetic sibling subtrees in a deliberately different insertion order.</summary>
    private static async Task<Guid> SeedAsync(
        ScopedNestedSet<OrderingNode, int> tree
    )
    {
        var treeId = Guid.NewGuid();
        await tree.InsertRootAsync(Node(1, "Root"), treeId, CancellationToken.None);
        await tree.InsertChildAsync(Node(4, "Charlie"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(5, "Leaf"), 3, CancellationToken.None);

        return treeId;
    }

    /// <summary>Reads all persisted payload and structure in stable key order for rollback comparisons.</summary>
    private static Task<OrderingNode[]> SnapshotAsync(
        OrderingContext context
    ) => context
        .Set<OrderingNode>()
        .AsNoTracking()
        .OrderBy(node => node.Id)
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Compares complete persisted values without relying on reference equality.</summary>
    private static void AssertSnapshot(
        IEnumerable<OrderingNode> expected,
        IEnumerable<OrderingNode> actual
    ) => Assert.Equal(
        expected.Select(node => (node.Id, node.Scope, node.TreeId, node.ParentId, node.Name, node.Priority,
            node.Payload, node.Left, node.Right, node.Depth, node.Position)),
        actual.Select(node => (node.Id, node.Scope, node.TreeId, node.ParentId, node.Name, node.Priority, node.Payload,
            node.Left, node.Right, node.Depth, node.Position)));

    /// <summary>Checks interval and adjacency invariants independently of the library's inspector.</summary>
    private static async Task AssertForestAsync(
        OrderingContext context,
        int scope = 1
    )
    {
        var forest = await Nodes(context, scope)
            .ToArrayAsync(CancellationToken.None);

        Assert.NotEmpty(forest);

        // WHY: Independent trees reuse local bounds, so interval ancestry is valid only within complete identity.
        foreach (var group in forest.GroupBy(node => (node.Scope, node.TreeId)))
        {
            var nodes = group
                .OrderBy(node => node.Left)
                .ToArray();

            var root = Assert.Single(nodes, node => node.ParentId is null);
            Assert.Equal(0, root.Position);
            Assert.Equal(0, root.Depth);
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

                Assert.Equal(
                    ancestors.LastOrDefault()
                        ?.Id,
                    node.ParentId);
                Assert.Equal(ancestors.Length, node.Depth);
                Assert.Equal(0, (node.Right - node.Left + 1) % 2);
            }

            foreach (var siblings in nodes.GroupBy(node => node.ParentId))
            {
                Assert.Equal(
                    Enumerable
                        .Range(0, siblings.Count())
                        .Select(value => (long)value),
                    siblings.Select(node => node.Position));
            }

            var report = await Tree(context, scope)
                .InTree(group.Key.TreeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

            Assert.True(report.IsValid);
        }
    }

    /// <summary>Starts a caller-owned transaction at the provider's supported isolation level.</summary>
    private static Task<IDbContextTransaction> BeginAsync(
        OrderingContext context
    ) => context.Database.BeginTransactionAsync(
        context.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
        CancellationToken.None);
}
