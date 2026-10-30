namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Independent owner ranges preserve global tenant visibility for matching and rejected bindings.</summary>
    /// <param name="includeSelf">Whether the selected source may also be an owner.</param>
    /// <param name="matchingScope">Whether the explicit Scope agrees with the application filter.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IndexedAncestorsRetainApplicationTenantFilter(bool includeSelf, bool matchingScope)
    {
        // Arrange
        var tenant = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexTenantQueryContext>(Engine,
            static options => new AncestorIndexTenantQueryContext(options), probe);

        foreach (var scope in new[] { tenant, foreignTenant })
        {
            await context.AddRangeAsync(Enumerable.Range(1, 3).Select(id => new CompositionNode
            {
                Scope = scope,
                TreeId = tree,
                Id = id,
                ParentId = id == 1 ? null : id - 1,
                Left = id,
                Right = 7 - id,
                Depth = id - 1,
            }), CancellationToken.None);
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        context.Tenant = tenant;
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(matchingScope ? tenant : foreignTenant);
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
                (source, owner, distance) => new { owner.Id, owner.Scope })
            : hierarchy.SelectAncestors(node => node.Id == 3,
                (source, owner, distance) => new { owner.Id, owner.Scope });

        int[] expected = !matchingScope ? [] : includeSelf ? [1, 2, 3] : [1, 2];
        probe.Reset();

        // Act
        var actual = await query.OrderBy(owner => owner.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expected, actual.Select(owner => owner.Id));
        Assert.All(actual, owner => Assert.Equal(tenant, owner.Scope));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
