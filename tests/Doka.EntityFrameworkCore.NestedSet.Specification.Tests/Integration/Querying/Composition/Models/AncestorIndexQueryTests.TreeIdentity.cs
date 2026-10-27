namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Forest owner equality follows the mapped case-sensitive converted tree column rather than broader CLR equality.</summary>
    /// <param name="includeSelf">Whether leaf owners participate.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedForestKeepsConvertedTreeAliasesDatabaseDistinct(bool includeSelf)
    {
        // Arrange
        var first = Interlocked.Add(ref s_mappingIdentity, 10);
        var token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var tree = new BroadTreeId(token + "a");
        var otherTree = new BroadTreeId(token + "A");
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexMappedContext>(Engine,
            static options => new AncestorIndexMappedContext(options), probe);

        foreach (var (identity, offset) in new[] { (tree, 0), (otherTree, 3) })
        {
            await context.AddRangeAsync(Enumerable.Range(0, 3).Select(depth => new BroadTreeIdNode
            {
                Id = first + offset + depth,
                ParentId = depth == 0 ? null : first + offset + depth - 1,
                TreeId = identity,
                Left = depth + 1,
                Right = 6 - depth,
                Depth = depth,
                Name = $"Node {depth}",
            }), CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var selected = first + 2;
        var hierarchy = context.NestedSet<BroadTreeIdNode>();
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == selected, (source, owner, distance) => owner.Id)
            : hierarchy.SelectAncestors(node => node.Id == selected, (source, owner, distance) => owner.Id);

        int[] expected = includeSelf ? [first, first + 1, first + 2] : [first, first + 1];
        probe.Reset();

        // Act
        var actual = await query.OrderBy(key => key).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.True(tree.Equals(otherTree));
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
