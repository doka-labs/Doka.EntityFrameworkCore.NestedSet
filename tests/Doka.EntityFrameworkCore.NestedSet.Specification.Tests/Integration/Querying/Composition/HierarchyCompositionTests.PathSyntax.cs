namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionTests
{
    /// <summary>Shared fixed path syntax cannot retain another rule, row alias or nearest decision.</summary>
    /// <param name="reverseConstruction">Whether the rejected branch is prepared in the opposite order.</param>
    /// <param name="rowAware">Whether the edge rule also depends on its enclosing source row.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InterleavedPathAndNearestRulesRetainTheirOwnAliases(
        bool reverseConstruction,
        bool rowAware
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var rows = hierarchy.SelectSubtrees(node => node.Id == 1,
            (anchor, node, distance) => new { Anchor = anchor, Node = node });

        var firstBlocked = reverseConstruction ? 5 : 2;
        var secondBlocked = reverseConstruction ? 2 : 5;
        var first = Build(firstBlocked);
        var second = Build(secondBlocked);
        int[] firstExpected = firstBlocked == 2 ? [1, 5, 6] : [1];
        int[] secondExpected = secondBlocked == 2 ? [1, 5, 6] : [1];

        // Act
        var secondResult = await second.ToArrayAsync(CancellationToken.None);
        var firstResult = await first.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(firstExpected, firstResult);
        Assert.Equal(secondExpected, secondResult);
        Assert.Equal(2, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());

        IQueryable<int> Build(int blocked)
        {
            var paths = rowAware
                ? hierarchy.WherePathEdges(rows, row => row.Anchor, row => row.Node,
                    (row, parent, child) => child.Id != blocked || row.Node.Id == 0)
                : hierarchy.WherePathEdges(rows, row => row.Anchor, row => row.Node,
                    (row, parent, child) => child.Id != blocked);

            // WHY: Node 2 is a nearer rejecting candidate than the matching root. The second query
            // leaves that branch open, so sharing syntax must not accidentally enable an ancestor fallback.
            return hierarchy.WhereNearestAncestorOrSelf(paths, row => row.Node,
                    (row, owner) => owner.Candidate, (row, owner) => owner.Matches)
                .Select(row => row.Node.Id)
                .OrderBy(id => id);
        }
    }
}
