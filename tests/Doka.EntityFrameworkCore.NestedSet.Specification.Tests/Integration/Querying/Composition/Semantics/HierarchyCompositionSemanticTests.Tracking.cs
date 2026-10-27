namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>The terminal EF tracking policy applies across all aliases in a composed entity projection.</summary>
    /// <param name="policy">No tracking, tracking, or no-tracking identity resolution.</param>
    [Theory]
    [InlineData(CompositionTrackingPolicy.NoTracking)]
    [InlineData(CompositionTrackingPolicy.Tracking)]
    [InlineData(CompositionTrackingPolicy.IdentityResolution)]
    public async Task FinalTrackingPolicyControlsRepeatedEntityOccurrences(
        CompositionTrackingPolicy policy
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectAncestors(
                node => node.Id == 3 || node.Id == 4,
                (source, ancestor, distance) => new { Source = source.Id, Ancestor = ancestor })
            .Where(row => row.Ancestor.Id == 1)
            .OrderBy(row => row.Source);

        var query = policy switch
        {
            CompositionTrackingPolicy.NoTracking => pairs.AsNoTracking(),
            CompositionTrackingPolicy.Tracking => pairs.AsTracking(),
            _ => pairs.AsNoTrackingWithIdentityResolution(),
        };

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3, 4], actual.Select(row => row.Source));
        Assert.Equal(policy != CompositionTrackingPolicy.NoTracking,
            ReferenceEquals(actual[0].Ancestor, actual[1].Ancestor));
        Assert.Equal(policy == CompositionTrackingPolicy.Tracking ? 1 : 0,
            context.ChangeTracker.Entries<CompositionNode>().Count());

        Assert.Equal(policy == CompositionTrackingPolicy.NoTracking ? 2 : 1, probe.MaterializedNodes);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Tracking reuses existing instances while scalar columns retain SQL snapshot values.</summary>
    [Fact]
    public async Task TrackingDoesNotRefreshAnExistingEntityFromTheQuerySnapshot()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var tracked = await context.Set<CompositionNode>()
            .SingleAsync(node => node.Scope == scope && node.Id == 1, CancellationToken.None);

        tracked.Matches = false;
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var actual = await hierarchy.SelectAncestors(
                node => node.Id == 3,
                (source, ancestor, distance) => new
                {
                    Ancestor = ancestor,
                    DatabaseMatches = ancestor.Matches,
                })
            .Where(row => row.Ancestor.Id == 1 && row.DatabaseMatches)
            .AsTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Same(tracked, actual.Ancestor);
        Assert.False(actual.Ancestor.Matches);
        Assert.True(actual.DatabaseMatches);
        Assert.Equal(1, context.ChangeTracker.Entries<CompositionNode>().Count());
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Scalar projections materialize no hierarchy objects when the final query requests tracking.</summary>
    [Fact]
    public async Task ScalarProjectionDoesNotAddTrackedEntities()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var actual = await hierarchy.SelectSubtrees(
                node => node.Id == 2,
                (anchor, node, distance) => new { Anchor = anchor.Id, Node = node.Id, Distance = distance })
            .AsTracking()
            .OrderBy(row => row.Node)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([(2, 2, 0), (2, 3, 1), (2, 4, 1)],
            actual.Select(row => (row.Anchor, row.Node, row.Distance)));

        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(1, probe.CommandCount);
    }
}
