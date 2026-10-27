namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Visible edge counts cannot accept a missing parent or a parent that skips a stored depth.</summary>
    /// <param name="parentId">The leaf's valid, skipped, missing, or foreign-tree direct parent.</param>
    /// <param name="nearest">Whether nearest-owner or explicit path semantics require the complete path.</param>
    [Theory]
    [InlineData(12, false)]
    [InlineData(11, false)]
    [InlineData(null, false)]
    [InlineData(5, false)]
    [InlineData(12, true)]
    [InlineData(11, true)]
    [InlineData(null, true)]
    [InlineData(5, true)]
    public async Task CompletePathRequiresEveryActualDirectParentAtTheNextDepth(
        int? parentId,
        bool nearest
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var tree = Guid.NewGuid();
        await context.AddRangeAsync(
        [
            new CompositionNode
            {
                Scope = scope, TreeId = tree, Id = 11, Left = 1, Right = 6, Candidate = true,
            },
            new CompositionNode
            {
                Scope = scope, TreeId = tree, Id = 12, ParentId = 11, Left = 2, Right = 5, Depth = 1,
            },
            new CompositionNode
            {
                Scope = scope, TreeId = tree, Id = 13, ParentId = parentId, Left = 3, Right = 4, Depth = 2,
            },
        ], CancellationToken.None);

        // WHY: The fixture boundary deliberately admits corrupt structure so the query must reject its incomplete path.
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectSubtrees(node => node.Id == 11,
                (upper, node, distance) => new { Upper = upper, Node = node })
            .Where(row => row.Node.Id == 13);

        // Act
        var query = nearest
            ? hierarchy.WhereNearestAncestorOrSelf(pairs, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => true)
            : hierarchy.WherePathEdges(pairs, row => row.Upper, row => row.Node,
                (row, parent, child) => true);

        var actual = await query.Select(row => row.Node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = parentId == 12 ? [13] : [];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }
}
