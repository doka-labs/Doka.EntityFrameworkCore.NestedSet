namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Self is the nearest eligible owner at every structural depth, before its value is checked.</summary>
    /// <param name="nodeId">The root or leaf selected as its own candidate.</param>
    /// <param name="accepted">Whether that selected owner's value is accepted.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public async Task NearestSelfHasNoMatchingAncestorFallback(
        int nodeId,
        bool accepted
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == nodeId);

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                source,
                node => node,
                (node, owner) => true,
                (node, owner) => owner.Id != node.Id || accepted)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = accepted ? [nodeId] : [];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>A missing eligible owner cannot be substituted with the root or a default-valued key.</summary>
    /// <param name="nearest">Whether the selection uses nearest or existential owner semantics.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoEligibleOwnerProducesNoRows(
        bool nearest
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 3);

        // Act
        var query = nearest
            ? hierarchy.WhereNearestAncestorOrSelf(source, node => node, (node, owner) => false, (node, owner) => true)
            : hierarchy.WhereAnyAncestorOrSelf(source, node => node, (node, owner) => false);

        var actual = await query.Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Candidate type and accepted value remain correlated to each outer payload occurrence.</summary>
    [Fact]
    public async Task NearestCandidateSelectionUsesEachOuterRowsPayload()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var nodes = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 3);
        var source = nodes.Select(node => new { Node = node, CandidateId = 1, AcceptedId = 1 })
            .Concat(nodes.Select(node => new { Node = node, CandidateId = 2, AcceptedId = 1 }));

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                source,
                row => row.Node,
                (row, owner) => owner.Id == row.CandidateId,
                (row, owner) => owner.Id == row.AcceptedId)
            .Select(row => row.CandidateId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1], actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>Conjoined owner criteria cannot borrow a value from a different ancestor.</summary>
    /// <param name="nearest">Whether the owner is selected before the conjunction is evaluated.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerCriteriaMustMatchTheSameAncestor(
        bool nearest
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 3);

        // Act
        var query = nearest
            ? hierarchy.WhereNearestAncestorOrSelf(
                source, node => node, (node, owner) => owner.Candidate,
                (node, owner) => owner.Id == 2 && owner.Matches)
            : hierarchy.WhereAnyAncestorOrSelf(
                source, node => node, (node, owner) => owner.Id == 2 && owner.Matches);

        var actual = await query.Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Nearest-owner selection does not silently interpret application periods as structural edges.</summary>
    [Fact]
    public async Task NearestDoesNotInterpretApplicationPeriodsAsStructuralEdges()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectSubtrees(
            node => node.Id == 2,
            (origin, node, distance) => new { Origin = origin, Node = node });

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                pairs,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => owner.Id == 2 && !owner.Matches && owner.ToDate != null)
            .Select(row => row.Node.Id)
            .OrderBy(id => id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([2, 3, 4], actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Strict nearest selection follows parent distance despite opposing key and date orders.</summary>
    [Fact]
    public async Task StrictNearestUsesStructuralDepthWithNonMonotonicKeys()
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
                Scope = scope, TreeId = tree, Id = 90, Left = 1, Right = 6, Candidate = true,
                FromDate = new DateOnly(2029, 1, 1),
            },
            new CompositionNode
            {
                Scope = scope, TreeId = tree, Id = 7, ParentId = 90, Left = 2, Right = 5, Depth = 1,
                Candidate = true, FromDate = new DateOnly(2020, 1, 1),
            },
            new CompositionNode
            {
                Scope = scope, TreeId = tree, Id = 40, ParentId = 7, Left = 3, Right = 4, Depth = 2,
                Candidate = true,
            },
        ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectAncestors(node => node.Id == 40,
            (source, owner, distance) => new { Source = source, Owner = owner, Distance = distance });

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                pairs,
                row => row.Source,
                (row, owner) => owner.Candidate && owner.Id != row.Source.Id,
                (row, owner) => owner == row.Owner)
            .Select(row => new { Id = row.Owner.Id, row.Distance })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        var selected = Assert.Single(actual);
        Assert.Equal((7, 1), (selected.Id, selected.Distance));
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>A failing self candidate cannot be bypassed by expanding a farther matching projected pivot.</summary>
    /// <param name="strictCandidates">Whether self is explicitly excluded from the candidate set.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectedNearestIsChosenBeforeSubtreeExpansion(
        bool strictCandidates
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectAncestorsOrSelf(node => node.Id == 2,
            (source, pivot, distance) => new { Source = source, Pivot = pivot });

        // Act
        var selected = hierarchy.WhereNearestAncestorOrSelf(
            pairs,
            row => row.Source,
            (row, owner) => owner.Candidate && (!strictCandidates || owner.Id != row.Source.Id),
            (row, owner) => owner == row.Pivot && owner.Matches);

        var targets = hierarchy.SelectSubtrees(pivot => selected.Any(row => row.Pivot == pivot),
            (pivot, node, distance) => new { Pivot = pivot, Node = node });

        var actual = await selected.Join(targets, row => row.Pivot, row => row.Pivot,
                (left, right) => right.Node.Id)
            .OrderBy(id => id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = strictCandidates ? [1, 2, 3, 4, 5, 6] : [];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }
}
