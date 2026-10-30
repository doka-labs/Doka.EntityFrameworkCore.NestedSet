namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Explicit filter opt-out restores visibility and retains technical identity boundaries.</summary>
    /// <param name="composer">The endpoint filter exercised against the mixed source.</param>
    [Theory]
    [InlineData(CompositionEndpointFilter.Path)]
    [InlineData(CompositionEndpointFilter.Any)]
    [InlineData(CompositionEndpointFilter.Nearest)]
    public async Task IgnoringVisibilityKeepsScopeAndTreeBindings(
        CompositionEndpointFilter composer
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var foreignScope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 3);
        var localTree = await context.Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id == 1)
            .Select(node => node.TreeId)
            .SingleAsync(CancellationToken.None);

        var otherTree = Guid.NewGuid();
        await context.AddRangeAsync(
        [
            NewRoot(foreignScope, localTree, 3),
            NewRoot(scope, otherTree, 42),
        ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(localTree);
        var source = context.Set<CompositionNode>()
            .IgnoreQueryFilters()
            .Where(node => node.Id == 3 || node.Id == 42);

        // Act
        var query = composer switch
        {
            CompositionEndpointFilter.Path => hierarchy.WherePathEdges(
                source, node => node, node => node, (node, parent, child) => true),
            CompositionEndpointFilter.Any => hierarchy.WhereAnyAncestorOrSelf(
                source, node => node, (node, owner) => true),
            _ => hierarchy.WhereNearestAncestorOrSelf(
                source, node => node, (node, owner) => true, (node, owner) => true),
        };

        var actual = await query.Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3], actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>A visible endpoint in another tree cannot pass a bound-tree query even with equal bounds.</summary>
    [Fact]
    public async Task ForeignTreeEndpointCannotAdoptMatchingLocalBounds()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var tree = await context.Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id == 1)
            .Select(node => node.TreeId)
            .SingleAsync(CancellationToken.None);

        var otherTree = Guid.NewGuid();
        var template = await context.Set<CompositionNode>()
            .AsNoTracking()
            .Where(node => node.Scope == scope)
            .ToArrayAsync(CancellationToken.None);

        await context.AddRangeAsync(template.Select(node => new CompositionNode
        {
            Scope = scope,
            TreeId = otherTree,
            Id = node.Id + 100,
            ParentId = node.ParentId + 100,
            Left = node.Left,
            Right = node.Right,
            Depth = node.Depth,
            Position = node.Position,
            Candidate = node.Candidate,
            Matches = node.Matches,
        }), CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(tree);
        var foreign = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 103);

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                foreign,
                node => node,
                (node, owner) => true,
                (node, owner) => true)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Outer-join nulls remove their own occurrences while valid endpoints retain their bag.</summary>
    /// <param name="composer">The endpoint filter applied after the null-producing join.</param>
    [Theory]
    [InlineData(CompositionEndpointFilter.Path)]
    [InlineData(CompositionEndpointFilter.Any)]
    [InlineData(CompositionEndpointFilter.Nearest)]
    public async Task MissingEndpointOnlyRemovesItsOwnOccurrences(
        CompositionEndpointFilter composer
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var nodes = context.Set<CompositionNode>().Where(node => node.Scope == scope);
        var once = nodes.Where(node => node.Id == 3 || node.Id == 4)
            .LeftJoin(
                nodes.Where(node => node.Id == 4),
                origin => origin.Id,
                endpoint => endpoint.Id,
                (origin, endpoint) => new { Origin = origin, Node = endpoint! });

        var source = once.Concat(once);

        // Act
        var query = composer switch
        {
            CompositionEndpointFilter.Path => hierarchy.WherePathEdges(
                source, row => row.Origin, row => row.Node, (row, parent, child) => true),
            CompositionEndpointFilter.Any => hierarchy.WhereAnyAncestorOrSelf(
                source, row => row.Node, (row, owner) => true),
            _ => hierarchy.WhereNearestAncestorOrSelf(
                source, row => row.Node, (row, owner) => true, (row, owner) => true),
        };

        var actual = await query.Select(row => row.Origin.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([4, 4], actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Selected aliases retain their own identity even when a row contains several mapped entities.</summary>
    /// <param name="selectPivot">Whether the predicate observes the projected pivot rather than the source.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerPredicateUsesTheExplicitlySelectedAlias(
        bool selectPivot
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectAncestors(
            node => node.Id == 3,
            (source, pivot, distance) => new { Source = source, Pivot = pivot });

        // Act
        var query = selectPivot
            ? hierarchy.WhereAnyAncestorOrSelf(pairs, row => row.Pivot, (row, owner) => owner.Id == 2)
            : hierarchy.WhereAnyAncestorOrSelf(pairs, row => row.Source, (row, owner) => owner.Id == 2);

        var actual = await query.Select(row => row.Pivot.Id).OrderBy(id => id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = selectPivot ? [2] : [1, 2];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Creates an independent one-node tree with a stable tree identifier and no application role.</summary>
    private static CompositionNode NewRoot(
        Guid scope,
        Guid tree,
        int key
    ) => new()
    {
        Scope = scope,
        TreeId = tree,
        Id = key,
        Left = 1,
        Right = 2,
        Candidate = true,
        Matches = true,
    };
}
