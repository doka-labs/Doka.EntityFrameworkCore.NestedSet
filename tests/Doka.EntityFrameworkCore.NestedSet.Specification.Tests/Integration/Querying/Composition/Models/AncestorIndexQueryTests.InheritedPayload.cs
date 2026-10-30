namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Late owner rehydration preserves polymorphic entity types and payload stored outside the coordinate source.</summary>
    /// <param name="tpt">Whether derived payload occupies separate tables.</param>
    /// <param name="includeSelf">Whether the selected leaf entity is returned as well.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IndexedOwnerRehydrationPreservesInheritedPayloadAndConcreteTypes(bool tpt, bool includeSelf)
    {
        // Arrange
        var first = Interlocked.Add(ref s_mappingIdentity, 10);
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using DbContext context = tpt
            ? await _fixture.CreateContextAsync<AncestorIndexTptQueryContext>(Engine,
                static options => new AncestorIndexTptQueryContext(options), probe)
            : await _fixture.CreateContextAsync<AncestorIndexMappedContext>(Engine,
                static options => new AncestorIndexMappedContext(options), probe);

        await SeedMappedQueryAsync<InheritanceNode>(context, tree, scope, first, includeSelf,
            depth => tpt
                ? depth == 1 ? new TptMetricNode() : new TptFolderNode()
                : depth == 1 ? new TphMetricNode() : new TphFolderNode());

        var hierarchy = context.NestedSet<InheritanceNode>().InTree(tree);
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Depth == 2, (source, owner, distance) => owner)
            : hierarchy.SelectAncestors(node => node.Depth == 2, (source, owner, distance) => owner);

        Type[] expectedTypes = tpt
            ? [typeof(TptFolderNode), typeof(TptMetricNode), typeof(TptFolderNode)]
            : [typeof(TphFolderNode), typeof(TphMetricNode), typeof(TphFolderNode)];

        probe.Reset();

        // Act
        var actual = await query.OrderBy(owner => owner.Depth).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expectedTypes.Take(includeSelf ? 3 : 2), actual.Select(owner => owner.GetType()));
        Assert.All(actual, owner => Assert.Equal($"mapped-{owner.Depth}", owner.Name));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
