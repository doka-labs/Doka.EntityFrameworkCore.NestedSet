namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Selected anchors stay unique while overlapping anchors retain separate node contributions.</summary>
    /// <param name="empty">Whether the consumer supplied no anchor identities.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedRequestedKeysDoNotDuplicateAnchorsOrCollapseOverlap(
        bool empty
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        int[] requested = empty ? [] : [1, 2, 2];

        // Act
        var actual = await hierarchy.SelectSubtrees(anchor => Enumerable.Contains(requested, anchor.Id),
                (anchor, node, distance) => new { Anchor = anchor.Id, Node = node.Id })
            .Where(row => row.Node == 3)
            .OrderBy(row => row.Anchor)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        (int Anchor, int Node)[] expected = empty ? [] : [(1, 3), (2, 3)];

        Assert.Equal(expected, actual.Select(row => (row.Anchor, row.Node)));
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>Only an explicit consumer Distinct may collapse the chosen occurrence grain.</summary>
    /// <param name="grain">The original bag, target, source-target, or complete walk distinctness.</param>
    [Theory]
    [InlineData(CompositionDistinctGrain.Bag)]
    [InlineData(CompositionDistinctGrain.Target)]
    [InlineData(CompositionDistinctGrain.SourceTarget)]
    [InlineData(CompositionDistinctGrain.Walk)]
    public async Task ExplicitDistinctUsesOnlyTheChosenResultGrain(
        CompositionDistinctGrain grain
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var first = hierarchy.SelectAncestors(node => node.Id == 3 || node.Id == 4,
            (source, pivot, distance) => new { Source = source, Pivot = pivot });

        var second = hierarchy.SelectSubtrees(pivot => first.Any(row => row.Pivot == pivot),
            (pivot, target, distance) => new { Pivot = pivot, Target = target });

        // Act
        var rows = first.Join(second, row => row.Pivot, row => row.Pivot,
                (left, right) => new { left.Source, left.Pivot, right.Target })
            .Where(row => row.Target.Id == 3)
            .Select(row => new
            {
                Source = grain == CompositionDistinctGrain.Target ? 0 : row.Source.Id,
                Pivot = grain == CompositionDistinctGrain.Target || grain == CompositionDistinctGrain.SourceTarget
                    ? 0
                    : row.Pivot.Id,
                Target = row.Target.Id,
            });

        if (grain != CompositionDistinctGrain.Bag)
        {
            rows = rows.Distinct();
        }

        var actual = await rows.OrderBy(row => row.Source).ThenBy(row => row.Pivot)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        (int Source, int Pivot, int Target)[] expected = grain switch
        {
            CompositionDistinctGrain.Target => [(0, 0, 3)],
            CompositionDistinctGrain.SourceTarget => [(3, 0, 3), (4, 0, 3)],
            _ => [(3, 1, 3), (3, 2, 3), (4, 1, 3), (4, 2, 3)],
        };

        Assert.Equal(expected, actual.Select(row => (row.Source, row.Pivot, row.Target)));
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>EXISTS filters one source occurrence while a join preserves every matching pivot occurrence.</summary>
    /// <param name="useExistence">Whether the consumer requests existence instead of each joined proof.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinAndExistenceHaveDifferentMultiplicity(
        bool useExistence
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var sources = context.Set<CompositionNode>()
            .Where(node => node.Scope == scope && (node.Id == 3 || node.Id == 4));

        var pairs = hierarchy.SelectAncestors(node => node.Id == 3 || node.Id == 4,
            (source, pivot, distance) => new { Source = source, Pivot = pivot });

        // Act
        var query = useExistence
            ? sources.Where(source => pairs.Any(row => row.Source == source)).Select(node => node.Id)
            : sources.Join(pairs, node => node, row => row.Source, (node, pair) => node.Id);

        var actual = await query.OrderBy(id => id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = useExistence ? [3, 4] : [3, 3, 4, 4];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Every pair shape supports explicit source or projected-node owner selection.</summary>
    /// <param name="pairShape">Subtree, strict ancestors, or inclusive ancestors.</param>
    /// <param name="projectedEndpoint">Whether the owner query applies to the second pair endpoint.</param>
    /// <param name="nearest">Whether candidate selection must precede the owner value predicate.</param>
    [Theory]
    [InlineData(CompositionPairShape.Subtrees, false, false)]
    [InlineData(CompositionPairShape.Subtrees, false, true)]
    [InlineData(CompositionPairShape.Subtrees, true, false)]
    [InlineData(CompositionPairShape.Subtrees, true, true)]
    [InlineData(CompositionPairShape.Ancestors, false, false)]
    [InlineData(CompositionPairShape.Ancestors, false, true)]
    [InlineData(CompositionPairShape.Ancestors, true, false)]
    [InlineData(CompositionPairShape.Ancestors, true, true)]
    [InlineData(CompositionPairShape.AncestorsOrSelf, false, false)]
    [InlineData(CompositionPairShape.AncestorsOrSelf, false, true)]
    [InlineData(CompositionPairShape.AncestorsOrSelf, true, false)]
    [InlineData(CompositionPairShape.AncestorsOrSelf, true, true)]
    public async Task PairShapesUseTheSelectedOwnerEndpoint(
        CompositionPairShape pairShape,
        bool projectedEndpoint,
        bool nearest
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = pairShape switch
        {
            CompositionPairShape.Subtrees => hierarchy.SelectSubtrees(node => node.Id == 2,
                (source, other, distance) => new { Source = source, Other = other }),
            CompositionPairShape.Ancestors => hierarchy.SelectAncestors(node => node.Id == 3,
                (source, other, distance) => new { Source = source, Other = other }),
            _ => hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
                (source, other, distance) => new { Source = source, Other = other }),
        };

        // Act
        var query = (projectedEndpoint, nearest) switch
        {
            (false, false) => hierarchy.WhereAnyAncestorOrSelf(pairs, row => row.Source,
                (row, owner) => owner.Candidate && owner.Matches),
            (true, false) => hierarchy.WhereAnyAncestorOrSelf(pairs, row => row.Other,
                (row, owner) => owner.Candidate && owner.Matches),
            (false, true) => hierarchy.WhereNearestAncestorOrSelf(pairs, row => row.Source,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
            _ => hierarchy.WhereNearestAncestorOrSelf(pairs, row => row.Other,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
        };

        var actual = await query.Select(row => row.Other.Id).OrderBy(id => id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = (pairShape, projectedEndpoint, nearest) switch
        {
            (CompositionPairShape.Subtrees, _, false) => [2, 3, 4],
            (CompositionPairShape.Ancestors, _, false) => [1, 2],
            (CompositionPairShape.AncestorsOrSelf, _, false) => [1, 2, 3],
            (CompositionPairShape.Ancestors or CompositionPairShape.AncestorsOrSelf, true, true) => [1],
            _ => [],
        };

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }
}
