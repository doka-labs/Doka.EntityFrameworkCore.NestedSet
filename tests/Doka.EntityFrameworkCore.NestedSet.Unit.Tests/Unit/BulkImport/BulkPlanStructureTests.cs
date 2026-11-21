namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies complete wide bulk plans without changing tracked state.</summary>
public sealed class BulkPlanStructureTests
{
    private const int NodeCount = 20_000;

    /// <summary>A wide plan retains every input even when each requires complete structural rollback state.</summary>
    /// <param name="prefilled">Whether inputs carry caller-owned non-sentinel structure before planning.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_WidePlan_RetainsGeometryAndCallerStructureWithoutTracking(
        bool prefilled
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

        await using var context = new TreeContext(options);
        var store = new NestedSetStore<TreeNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(TreeNode))!,
            1,
            Guid.Empty);

        var children = Enumerable
            .Range(2, NodeCount - 1)
            .Select(id => new NestedSetBranch<TreeNode>(
                new TreeNode
                {
                    NodeId = id,
                    Tree = prefilled
                        ? 7
                        : 0,
                    TreeId = prefilled
                        ? new Guid("11111111-1111-1111-1111-111111111111")
                        : Guid.Empty,
                    Parent = prefilled
                        ? 999
                        : null,
                    Start = prefilled
                        ? 17L
                        : 0L,
                    End = prefilled
                        ? 18L
                        : 0L,
                    Depth = prefilled
                        ? 9
                        : 0,
                    Position = prefilled
                        ? 11L
                        : 0L,
                }))
            .ToArray();

        var root = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }, children);
        _ = context.Model;

        // Act
        var plan = new NestedSetBulkPlan<TreeNode, int, Guid, int>(
            store,
            [root],
            new HashSet<TreeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        plan.PrepareGeometry(new(1, default, 0, 0), ranks: null, CancellationToken.None);

        foreach (var child in children)
        {
            child.Entity.Tree = 1;
            child.Entity.TreeId = Guid.Empty;
            child.Entity.Parent = 1;
            child.Entity.Start = 2;
            child.Entity.End = 3;
            child.Entity.Depth = 1;
            child.Entity.Position = 0;
        }

        plan.Restore();

        // Assert
        Assert.Equal(NodeCount, plan.Nodes.Count);
        Assert.Equal(NodeCount * 2L, plan.Width);
        Assert.Equal((1L, NodeCount * 2L, 0, 0L),
            (plan.Nodes[0].Geometry.Left, plan.Nodes[0].Geometry.Right,
                plan.Nodes[0].Geometry.Depth, plan.Nodes[0].Geometry.Position));

        // WHY: Each preorder index independently determines the wide tree's expected child interval and rank.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 1; index < plan.Nodes.Count; index++)
        {
            var node = plan.Nodes[index];
            Assert.Same(children[index - 1].Entity, node.Entity);
            Assert.Equal((index * 2L, index * 2L + 1, 1, index - 1L),
                (node.Geometry.Left, node.Geometry.Right, node.Geometry.Depth, node.Geometry.Position));
            Assert.Equal(0, node.Parent);
            Assert.Equal(
                (prefilled ? 7 : 0,
                    prefilled ? new Guid("11111111-1111-1111-1111-111111111111") : Guid.Empty,
                    prefilled ? (int?)999 : null,
                    prefilled ? 17L : 0L, prefilled ? 18L : 0L,
                    prefilled ? 9 : 0, prefilled ? 11L : 0L),
                (node.Entity.Tree, node.Entity.TreeId, node.Entity.Parent,
                    node.Entity.Start, node.Entity.End, node.Entity.Depth, node.Entity.Position));
        }

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
