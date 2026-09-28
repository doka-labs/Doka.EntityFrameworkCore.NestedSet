namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies compact bulk geometry at the target sibling-group width.</summary>
public sealed class BulkPlanWidthTests
{
    private const int ChildCount = 1_000_000;

    /// <summary>One million direct children receive dense positions without recursive or per-child lists.</summary>
    [Fact]
    public async Task MillionChildPlanIsStackSafe()
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

        var children = new NestedSetBranch<TreeNode>[ChildCount];

        for (var index = 0; index < children.Length; index++)
        {
            children[index] = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = index + 2 });
        }

        var root = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }, children);
        var destination = new NestedSetPlacementResolver<TreeNode, int, Guid, int>.Destination(1, default, 0, 0);

        // Act
        var plan = new NestedSetBulkPlan<TreeNode, int, Guid, int>(
            store,
            [root],
            new HashSet<TreeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        plan.PrepareGeometry(destination, null, CancellationToken.None);

        // Assert
        var last = plan.Nodes[^1];
        Assert.Equal(ChildCount + 1, plan.Nodes.Count);

        Assert.Equal(
            (1L, (ChildCount + 1L) * 2, 0L),
            (plan.Nodes[0].Geometry.Left, plan.Nodes[0].Geometry.Right, plan.Nodes[0].Geometry.Position));

        Assert.Equal(
            (ChildCount * 2L, (ChildCount * 2L) + 1, ChildCount - 1L),
            (last.Geometry.Left, last.Geometry.Right, last.Geometry.Position));
    }
}
