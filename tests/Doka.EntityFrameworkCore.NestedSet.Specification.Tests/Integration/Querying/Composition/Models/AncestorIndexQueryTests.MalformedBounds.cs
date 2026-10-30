namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Leaf equality preserves all four original bound comparisons even when a split table stores malformed intervals.</summary>
    /// <param name="includeSelf">Whether the inclusive range comparisons are selected.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedSplitIntervalsRetainOriginalContainmentForMalformedWidths(bool includeSelf)
    {
        // Arrange
        var first = MalformedIntervalTestSupport.NextIdentity();
        var tree = Guid.NewGuid();
        var foreignTree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexMappedContext>(Engine,
            static options => new AncestorIndexMappedContext(options), probe);

        var bounds = MalformedIntervalTestSupport.Bounds();
        var nodes = await MalformedIntervalTestSupport.SeedAsync(context, first, tree, foreignTree, bounds);
        var hierarchy = context.NestedSet<EntitySplitNode>();
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(source => source.Id >= first && source.Id < first + bounds.Length,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id })
            : hierarchy.SelectAncestors(source => source.Id >= first && source.Id < first + bounds.Length,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id });

        // WHY: This independent oracle states the original four comparisons directly, including
        // strict overlap guards. It must reject zero/reversed leaf self-pairs without assuming valid input,
        // and native width guards must retain both extremes of the signed bound domain.
        var expected = nodes.Take(bounds.Length).SelectMany(source => nodes
                .Where(owner => owner.TreeId == source.TreeId
                    && (includeSelf ? owner.Left <= source.Left : owner.Left < source.Left)
                    && (includeSelf ? owner.Right >= source.Right : owner.Right > source.Right)
                    && source.Left < owner.Right && source.Right > owner.Left)
                .Select(owner => (Source: source.Id, Owner: owner.Id)))
            .OrderBy(pair => pair.Source).ThenBy(pair => pair.Owner).ToArray();

        probe.Reset();

        // Act
        var actual = await query.OrderBy(pair => pair.Source).ThenBy(pair => pair.Owner)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expected, actual.Select(pair => (pair.Source, pair.Owner)));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
