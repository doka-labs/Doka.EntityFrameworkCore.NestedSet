namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Moves a deep branch without recursion or losing descendant depth changes.</summary>
    [Fact]
    public async Task DeepSubtreeMoveUpdatesEveryDescendantDepth()
    {
        // Arrange
        const int count = 1024;
        var database = await _fixture.ResetAsync(Engine);
        await using (var seed = database.CreateContext())
        {
            // WHY: Construct valid adjacency directly so setup does not hide quadratic insertion work in this test.
            await seed.AddRangeAsync(
                Enumerable
                    .Range(1, count)
                    .Select(id => new TreeNode
                    {
                        NodeId = id,
                        Tree = 1,
                        Parent = id == 1 ? null : id - 1,
                        Start = id,
                        End = (2 * count) - id + 1,
                        Depth = id - 1,
                        Position = 0,
                    }),
                CancellationToken.None);

            await seed.SavePrecomputedHierarchyAsync(CancellationToken.None);
        }

        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var other = context
            .NestedSet<TreeNode>()
            .ForScope(2);

        await other.InsertRootAsync(new TreeNode { NodeId = count + 1 }, Guid.Empty, CancellationToken.None);
        await other.InsertChildAsync(new TreeNode { NodeId = count + 2 }, count + 1, CancellationToken.None);
        var beforeOther = await SnapshotAsync(context, 2);

        // Act
        await tree.DetachAsTreeAsync(2, Guid.NewGuid(), CancellationToken.None);

        // Assert
        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.Tree == 1)
            .OrderBy(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(count, nodes.Length);
        Assert.Equal((1, 2, 0), (nodes[0].Start, nodes[0].End, nodes[0].Depth));
        Assert.Null(nodes[1].Parent);
        Assert.Equal(0, nodes[1].Position);
        Assert.All(nodes.Skip(1), node => Assert.Equal(node.NodeId - 2, node.Depth));
        await AssertValidAsync(context, 1);
        Assert.Equal(beforeOther, await SnapshotAsync(context, 2));
    }
}
