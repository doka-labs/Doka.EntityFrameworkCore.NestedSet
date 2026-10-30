namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionTests
{
    /// <summary>A valid zero node key remains distinct from an absent selected candidate.</summary>
    /// <param name="rootEligible">Whether the zero-key root is an eligible matching owner.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroNodeKeyDoesNotConfuseMissingAndSelectedOwner(
        bool rootEligible
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options),
            probe,
            TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, probe.RecordMaterialization);
        var treeId = Guid.NewGuid();
        await context.AddRangeAsync(
        [
            new CompositionNode
            {
                Scope = scope,
                TreeId = treeId,
                Id = 0,
                Left = 1,
                Right = 4,
                Candidate = rootEligible,
                Matches = true,
            },
            new CompositionNode
            {
                Scope = scope,
                TreeId = treeId,
                Id = 7,
                ParentId = 0,
                Left = 2,
                Right = 3,
                Depth = 1,
                Matches = true,
            },
        ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id == 7)
            .Select(node => new { Node = node });

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                source,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => owner.Matches)
            .Select(row => row.Node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = rootEligible ? [7] : [];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>A closed incoming edge prunes its complete branch while leaving the empty self path valid.</summary>
    [Fact]
    public async Task ClosedDirectEdgePrunesCompleteBranch()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, closed: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var pairs = hierarchy.SelectSubtrees(
            anchor => anchor.Id == 1,
            (anchor, node, distance) => new { Anchor = anchor, Node = node, Distance = distance });

        var actual = await hierarchy.WherePathEdges(
                pairs,
                row => row.Anchor,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen && child.ParentId == parent.Id)
            .Select(row => row.Node.Id)
            .OrderBy(id => id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 5, 6], actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Endpoint projection may cross hidden nodes, while explicit complete-path validation cannot.</summary>
    /// <param name="requirePath">Whether the consumer asks for complete visible direct edges.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenIntermediateIsNotSilentlySkippedByPathValidation(
        bool requirePath
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var pairs = hierarchy.SelectSubtrees(
                anchor => anchor.Id == 1,
                (anchor, node, distance) => new { Anchor = anchor, Node = node, Distance = distance })
            .Where(row => row.Node.Id == 3);

        var query = requirePath
            ? hierarchy.WherePathEdges(pairs, row => row.Anchor, row => row.Node, (row, parent, child) => true)
            : pairs;

        var actual = await query.Select(row => row.Distance).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = requirePath ? [] : [2];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Existential owner matching may pass an outer match, while nearest selection cannot fall back.</summary>
    /// <param name="nearest">Whether the query selects the closest candidate before checking its payload.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NearestCandidateFailureDoesNotFallBackToMatchingAncestor(
        bool nearest
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id == 3)
            .Select(node => new { Node = node });

        // Act
        var query = nearest
            ? hierarchy.WhereNearestAncestorOrSelf(
                source,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => owner.Matches)
            : hierarchy.WhereAnyAncestorOrSelf(
                source,
                row => row.Node,
                (row, owner) => owner.Candidate && owner.Matches);

        var commandsBeforeExecution = probe.CommandCount;
        var actual = await query.Select(row => row.Node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, commandsBeforeExecution);
        int[] expected = nearest ? [] : [3];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Any can cross hidden intermediates, while nearest requires its selected owner's whole path.</summary>
    /// <param name="nearest">Whether complete owner-to-node visibility is required.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NearestRejectsHiddenIntermediateEvenWhenItIsNotACandidate(
        bool nearest
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id == 3)
            .Select(node => new { Node = node });

        // Act
        var query = nearest
            ? hierarchy.WhereNearestAncestorOrSelf(
                source,
                row => row.Node,
                (row, owner) => owner.Id == 1,
                (row, owner) => owner.Matches)
            : hierarchy.WhereAnyAncestorOrSelf(source, row => row.Node, (row, owner) => owner.Id == 1 && owner.Matches);

        var actual = await query.Select(row => row.Node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = nearest ? [] : [3];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>A passing nearest owner is accepted without requiring a path above that selected owner.</summary>
    [Fact]
    public async Task NearestStopsVisibilityCheckAtSelectedOwner()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 1);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id == 3)
            .Select(node => new { Node = node });

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                source,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => owner.Id == 2)
            .Select(row => row.Node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3], actual);
        Assert.Equal(1, probe.CommandCount);
    }
}
