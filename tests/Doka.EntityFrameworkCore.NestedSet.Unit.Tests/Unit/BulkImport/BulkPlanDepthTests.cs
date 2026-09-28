namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that bulk planning and geometry remain iterative at the target hierarchy depth.</summary>
public sealed class BulkPlanDepthTests
{
    private const int NodeCount = 100_000;

    /// <summary>One hundred thousand levels are planned and numbered without CLR call-stack recursion.</summary>
    [Fact]
    public async Task DeepPlanIsStackSafe()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        await using var context = new TreeContext(options);
        var store = new NestedSetStore<TreeNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(TreeNode))!,
            1,
            Guid.Empty);

        var branch = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = NodeCount });

        for (var id = NodeCount - 1; id >= 1; id--)
        {
            branch = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = id }, [branch]);
        }

        var destination = new NestedSetPlacementResolver<TreeNode, int, Guid, int>.Destination(1, default, 0, 0);

        // Act
        var plan = new NestedSetBulkPlan<TreeNode, int, Guid, int>(
            store,
            [branch],
            new HashSet<TreeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        plan.PrepareGeometry(destination, null, CancellationToken.None);

        // Assert
        Assert.Equal(NodeCount, plan.Nodes.Count);
        Assert.Equal(
            (1L, NodeCount * 2L, 0),
            (plan.Nodes[0].Geometry.Left, plan.Nodes[0].Geometry.Right, plan.Nodes[0].Geometry.Depth));

        Assert.Equal(
            (NodeCount, NodeCount + 1L, NodeCount - 1),
            (plan.Nodes[^1].Geometry.Left, plan.Nodes[^1].Geometry.Right, plan.Nodes[^1].Geometry.Depth));
    }
}
