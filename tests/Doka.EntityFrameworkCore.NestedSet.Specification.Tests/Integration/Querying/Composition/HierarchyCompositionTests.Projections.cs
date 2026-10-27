namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionTests
{
    /// <summary>A selected leaf contributes exactly its visible self and never a sibling.</summary>
    /// <param name="hidden">Whether the selected endpoint is hidden by the model's query filter.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeafSelectionPreservesSelfVisibilityAndEmptyDescendants(
        bool hidden
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: hidden ? 3 : null);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var actual = await hierarchy.SelectSubtrees(node => node.Id == 3,
                (anchor, node, distance) => new { Anchor = anchor.Id, Node = node.Id, Distance = distance })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(hidden ? [] : [(3, 3, 0)], actual.Select(row => (row.Anchor, row.Node, row.Distance)));
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Internal pair partitioning preserves overlapping origins and a legal terminal client projection.</summary>
    [Fact]
    public async Task OverlappingRootAndLeafPreserveClientProjectionAndOccurrences()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var actual = await hierarchy.SelectSubtrees(node => node.Id == 1 || node.Id == 3,
                (anchor, node, distance) => FormatPair(anchor.Id, node.Id, distance))
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["1:1:0", "1:2:1", "1:3:2", "1:4:2", "1:5:1", "1:6:2", "3:3:0"],
            actual.Order(StringComparer.Ordinal));

        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Requires legitimate top-level client evaluation after SQL has projected only scalar coordinates.</summary>
    private static string FormatPair(
        int anchor,
        int node,
        int distance
    ) => FormattableString.Invariant($"{anchor}:{node}:{distance}");

    /// <summary>Proves the materialization counter observes entities before scalar profiles rely on zero.</summary>
    [Fact]
    public async Task MaterializationObserverCountsActualEntityResults()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);

        // Act
        var actual = await context.Set<CompositionNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Scope == scope && node.Id == 3, CancellationToken.None);

        // Assert
        Assert.Equal(3, actual.Id);
        Assert.Equal(1, probe.MaterializedNodes);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Multiple anchors correlate three independent sources with each measurement's edge validity.</summary>
    /// <param name="month">The month selecting a fixture measurement date.</param>
    /// <param name="rootFirstInput">The root's expected first-input sum after branch pruning.</param>
    /// <param name="branchFirstInput">The inner anchor's expected first-input sum, excluding its incoming edge.</param>
    /// <param name="rootThirdInput">The root's expected independent third-input sum.</param>
    /// <param name="branchThirdInput">The inner anchor's expected independent third-input sum.</param>
    [Theory]
    [InlineData(10, 60, 30, 75, 40)]
    [InlineData(11, 50, 40, 55, 45)]
    public async Task MultipleAnchorsCorrelateThreeSourcesAndDateOnlyEdges(
        int month,
        int rootFirstInput,
        int branchFirstInput,
        int rootThirdInput,
        int branchThirdInput
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        await SeedReadingsAsync(context, scope);
        probe.Reset();
        var date = new DateOnly(2044, month, 1);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var pairs = hierarchy.SelectSubtrees(
            anchor => anchor.Id == 1 || anchor.Id == 2,
            (anchor, node, distance) => new { Anchor = anchor, Node = node, Distance = distance });

        // WHY: UNION ALL preserves independent fact rows without a three-table join multiplying their values.
        var readings = context.Set<CompositionInputOne>()
            .Select(reading => new
            {
                reading.Scope,
                reading.NodeId,
                reading.Date,
                FirstInput = reading.Amount,
                SecondInput = 0,
                ThirdInput = 0,
            })
            .Concat(context.Set<CompositionInputTwo>().Select(reading => new
            {
                reading.Scope,
                reading.NodeId,
                reading.Date,
                FirstInput = 0,
                SecondInput = reading.Amount,
                ThirdInput = 0,
            }))
            .Concat(context.Set<CompositionInputThree>().Select(reading => new
            {
                reading.Scope,
                reading.NodeId,
                reading.Date,
                FirstInput = 0,
                SecondInput = 0,
                ThirdInput = reading.Amount,
            }));

        var facts = pairs.Join(
            readings.Where(reading => reading.Date == date),
            pair => new { pair.Node.Scope, NodeId = pair.Node.Id },
            reading => new { reading.Scope, reading.NodeId },
            (pair, reading) => new
            {
                pair.Anchor,
                pair.Node,
                reading.Date,
                reading.FirstInput,
                reading.SecondInput,
                reading.ThirdInput,
            });

        var allowed = hierarchy.WherePathEdges(
            facts,
            row => row.Anchor,
            row => row.Node,
            (row, parent, child) => child.FromDate <= row.Date
                && (child.ToDate == null || row.Date <= child.ToDate));

        var query = allowed.GroupBy(row => row.Anchor.Id)
            .Select(group => new
            {
                Anchor = group.Key,
                FirstInput = group.Sum(row => row.FirstInput),
                SecondInput = group.Sum(row => row.SecondInput),
                ThirdInput = group.Sum(row => row.ThirdInput),
            })
            .OrderBy(row => row.Anchor);

        var commandsBeforeExecution = probe.CommandCount;
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, commandsBeforeExecution);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(
            [(1, rootFirstInput, rootFirstInput * 10, rootThirdInput), (2, branchFirstInput, branchFirstInput * 10, branchThirdInput)],
            actual.Select(row => (row.Anchor, row.FirstInput, row.SecondInput, row.ThirdInput)));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Strict ancestors expose source-relative distance without loading a source entity.</summary>
    [Fact]
    public async Task AncestorDistanceSupportsNearestFirstRank()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var query = hierarchy.SelectAncestors(
                node => node.Id == 3,
                (node, ancestor, distance) => new { Ancestor = ancestor.Id, Distance = distance })
            .OrderBy(row => row.Distance)
            .Skip(1)
            .Take(1);

        var commandsBeforeExecution = probe.CommandCount;
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, commandsBeforeExecution);
        Assert.Equal(1, probe.CommandCount);
        var secondAncestor = Assert.Single(actual);
        Assert.Equal((1, 2), (secondAncestor.Ancestor, secondAncestor.Distance));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Self remains distance zero while a hidden intermediate does not compress stored distance.</summary>
    [Fact]
    public async Task AncestorsOrSelfPreserveDistanceAcrossHiddenIntermediate()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var actual = await hierarchy.SelectAncestorsOrSelf(
                node => node.Id == 3,
                (node, ancestor, distance) => new { Ancestor = ancestor.Id, Distance = distance })
            .OrderBy(row => row.Distance)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([(3, 0), (1, 2)], actual.Select(row => (row.Ancestor, row.Distance)));
        Assert.Equal(1, probe.CommandCount);
    }
}
