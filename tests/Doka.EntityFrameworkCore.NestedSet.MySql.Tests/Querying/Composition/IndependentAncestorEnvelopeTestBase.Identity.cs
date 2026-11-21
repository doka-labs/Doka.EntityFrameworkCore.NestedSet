namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class IndependentAncestorEnvelopeTestBase
{
    /// <summary>Identical intervals in different trees cannot cross-match their sources or ancestors.</summary>
    [Fact]
    public async Task SelectAncestors_EqualBoundsInDifferentTrees_RetainExactAncestry()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(
            _fixture,
            Engine,
            scope,
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            hidden: null,
            closed: null,
            planProbe: null,
            chainNodes: null,
            ownerMode: CompositionTestData.ChainOwnerMode.RootValue);

        var secondTree = Guid.NewGuid();
        await context.AddRangeAsync(
            [
                Node(
                    scope,
                    secondTree,
                    101,
                    null,
                    1,
                    12,
                    0),
                Node(
                    scope,
                    secondTree,
                    102,
                    101,
                    2,
                    7,
                    1),
                Node(
                    scope,
                    secondTree,
                    103,
                    102,
                    3,
                    4,
                    2),
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id == 3 || node.Id == 103,
                (source, owner, distance) => new
                {
                    Source = source.Id,
                    Owner = owner.Id,
                    Distance = distance,
                });

        // Act
        var rows = await query
            .OrderBy(row => row.Source)
            .ThenBy(row => row.Owner)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3, 3, 103, 103], rows.Select(row => row.Source));
        Assert.Equal([1, 2, 101, 102], rows.Select(row => row.Owner));
        Assert.Equal([2, 1, 2, 1], rows.Select(row => row.Distance));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Unbound candidate access cannot introduce another scope with identical keys, tree and intervals.
    /// </summary>
    [Fact]
    public async Task SelectAncestors_EqualTreeKeysAndBoundsInAnotherScope_DoNotDuplicateOwners()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var foreignScope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(
            _fixture,
            Engine,
            scope,
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            hidden: null,
            closed: null,
            planProbe: null,
            chainNodes: null,
            ownerMode: CompositionTestData.ChainOwnerMode.RootValue);

        var tree = await context
            .Set<CompositionNode>()
            .Where(node => node.Scope == scope)
            .Select(node => node.TreeId)
            .FirstAsync(CancellationToken.None);

        await context.AddRangeAsync(
            [
                Node(
                    foreignScope,
                    tree,
                    1,
                    null,
                    1,
                    12,
                    0),
                Node(
                    foreignScope,
                    tree,
                    2,
                    1,
                    2,
                    7,
                    1),
                Node(
                    foreignScope,
                    tree,
                    3,
                    2,
                    3,
                    4,
                    2),
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id == 3,
                (source, owner, distance) => new
                {
                    Source = source.Scope,
                    Owner = owner.Id,
                    OwnerScope = owner.Scope,
                })
            .OrderBy(row => row.Owner);

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2], rows.Select(row => row.Owner));
        Assert.All(rows, row => Assert.Equal(scope, row.Source));
        Assert.All(rows, row => Assert.Equal(scope, row.OwnerScope));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Necessary source limits use no sentinel arithmetic near the maximum supported bound.</summary>
    [Fact]
    public async Task SelectAncestors_ExtremeBounds_RetainTheStrictAncestor()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<CompositionContext>(
            Engine,
            static options => new CompositionContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        await context.AddRangeAsync(
            [
                Node(
                    scope,
                    tree,
                    1,
                    null,
                    1,
                    long.MaxValue,
                    0),
                Node(
                    scope,
                    tree,
                    2,
                    1,
                    long.MaxValue - 2,
                    long.MaxValue - 1,
                    1),
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();
        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .SelectAncestors(
                node => node.Id == 2,
                (source, owner, distance) => new
                {
                    Source = source.Id,
                    Owner = owner.Id,
                    Distance = distance,
                });

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal(2, row.Source);
        Assert.Equal(1, row.Owner);
        Assert.Equal(1, row.Distance);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
