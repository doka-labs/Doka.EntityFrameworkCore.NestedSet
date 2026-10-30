namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Closed partition points preserve converted column equality without CLR ordering or broad domain equality.</summary>
    /// <param name="includeSelf">Whether the selected leaf also participates as an owner.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedForestKeepsConvertedScopeAliasesDatabaseDistinct(bool includeSelf)
    {
        // Arrange
        var token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var scope = new BroadScope(token + "a");
        var foreignScope = new BroadScope(token + "A");
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexScopeQueryContext>(Engine,
            static options => new AncestorIndexScopeQueryContext(options), probe);

        foreach (var identity in new[] { scope, foreignScope })
        {
            await context.AddRangeAsync(Enumerable.Range(1, 3).Select(id => new BroadScopeNode
            {
                Scope = identity,
                TreeId = tree,
                Id = id,
                ParentId = id == 1 ? null : id - 1,
                Left = id,
                Right = 7 - id,
                Depth = id - 1,
                Name = $"node-{id}",
            }), CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var hierarchy = context.NestedSet<BroadScopeNode>().ForScope(scope);
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
                (source, owner, distance) => new { owner.Id, owner.Scope })
            : hierarchy.SelectAncestors(node => node.Id == 3,
                (source, owner, distance) => new { owner.Id, owner.Scope });

        int[] expected = includeSelf ? [1, 2, 3] : [1, 2];
        probe.Reset();

        // Act
        var actual = await query.OrderBy(owner => owner.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.True(scope.Equals(foreignScope));
        Assert.Equal(expected, actual.Select(owner => owner.Id));
        Assert.All(actual, owner => Assert.Equal(scope.Value, owner.Scope.Value));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
