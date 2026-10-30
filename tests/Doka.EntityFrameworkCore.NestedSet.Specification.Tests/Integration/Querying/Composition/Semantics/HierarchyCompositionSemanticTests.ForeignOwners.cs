namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>A deeper foreign-tree candidate with overlapping bounds cannot affect scope-wide composition.</summary>
    /// <param name="shape">The pair projection or endpoint filter exercised without an InTree restriction.</param>
    [Theory]
    [InlineData("Ancestors")]
    [InlineData("Path")]
    [InlineData("Any")]
    [InlineData("Nearest")]
    public async Task DeeperForeignOwnerCannotAffectScopeWideSelection(
        string shape
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var foreignTree = Guid.NewGuid();
        await context.AddRangeAsync(Enumerable.Range(0, 8).Select(depth => new CompositionNode
        {
            Scope = scope,
            TreeId = foreignTree,
            Id = 101 + depth,
            ParentId = depth == 0 ? null : 100 + depth,
            Left = depth + 1,
            Right = 16 - depth,
            Depth = depth,
            Candidate = depth == 3,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 6);
        var pairs = hierarchy.SelectSubtrees(node => node.Id == 1,
                (upper, node, distance) => new { Upper = upper, Node = node })
            .Where(row => row.Node.Id == 6);

        // Act
        var query = shape switch
        {
            "Ancestors" => hierarchy.SelectAncestors(node => node.Id == 6,
                (node, upper, distance) => upper.Id),
            "Path" => hierarchy.WherePathEdges(pairs, row => row.Upper, row => row.Node,
                    (row, parent, child) => true)
                .Select(row => row.Node.Id),
            "Any" => hierarchy.WhereAnyAncestorOrSelf(source, node => node, (node, owner) => owner.Id == 104)
                .Select(node => node.Id),
            _ => hierarchy.WhereNearestAncestorOrSelf(source, node => node,
                    (node, owner) => owner.Candidate, (node, owner) => owner.Matches)
                .Select(node => node.Id),
        };

        var actual = await query.OrderBy(id => id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = shape switch
        {
            "Ancestors" => [1, 5],
            "Any" => [],
            _ => [6],
        };

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }
}
