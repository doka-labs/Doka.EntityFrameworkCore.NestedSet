namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionTests
{
    /// <summary>
    /// Aligned two-edge coordinates cannot bridge through another tree and back to the endpoint tree.
    /// </summary>
    /// <param name="foreignMiddle">Whether the direct middle parent belongs to another tree.</param>
    /// <param name="bindTree">Whether the query additionally binds its visible node set to the endpoint tree.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ShortPathRequiresTreeIdentityOnBothDirectEdges(bool foreignMiddle, bool bindTree)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var middleTree = foreignMiddle ? Guid.NewGuid() : treeId;
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, probe.RecordMaterialization);
        await context.AddRangeAsync(
        [
            new CompositionNode
            {
                Scope = scope,
                TreeId = treeId,
                Id = 1,
                Left = 1,
                Right = 6,
            },
            new CompositionNode
            {
                Scope = scope,
                TreeId = middleTree,
                Id = 2,
                ParentId = 1,
                Left = 2,
                Right = 5,
                Depth = 1,
            },
            new CompositionNode
            {
                Scope = scope,
                TreeId = treeId,
                Id = 3,
                ParentId = 2,
                Left = 3,
                Right = 4,
                Depth = 2,
            },
        ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var bound = hierarchy.InTree(treeId);

        // Act
        // WHY: Both endpoints share tree A and all depth/bounds checks pass. Checking only the last
        // child's parent key would incorrectly allow the forest path to cross A -> B -> A.
        var selected = bindTree
            ? bound.SelectSubtrees(node => node.Id == 1,
                (upper, lower, distance) => new { Upper = upper, Lower = lower })
            : hierarchy.SelectSubtrees(node => node.Id == 1,
                (upper, lower, distance) => new { Upper = upper, Lower = lower });

        var pairs = selected.Where(row => row.Lower.Id == 3);
        var paths = bindTree
            ? bound.WherePathEdges(pairs, row => row.Upper, row => row.Lower, (row, parent, child) => true)
            : hierarchy.WherePathEdges(pairs, row => row.Upper, row => row.Lower, (row, parent, child) => true);

        var actual = await paths.Select(row => row.Lower.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = foreignMiddle ? [] : [3];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
