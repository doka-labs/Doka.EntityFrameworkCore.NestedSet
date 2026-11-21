namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves native Scope membership while grouping transported NodeKeys.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>The same NodeKeys in another native Scope must not block the selected tree's mutation.</summary>
    [Fact]
    public async Task DeleteTreeAsync_NativeKeyCollection_KeepsDuplicateKeysInAnotherScopeSeparate()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ScopedScopeAliasContext>(
            Engine,
            static options => new ScopedScopeAliasContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affectedScope = $"guard-first-{root}";
        var otherScope = $"guard-second-{root}";
        var treeId = Guid.NewGuid();
        await NativeTrackedIdentityGuardTestSupport.SeedScopedNativeAsync(
            context,
            affectedScope,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        await NativeTrackedIdentityGuardTestSupport.SeedScopedNativeAsync(
            context,
            otherScope,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        var snapshots = await context
            .Set<ScopeAliasNode>()
            .AsNoTracking()
            .Where(node => node.Scope == otherScope)
            .ToArrayAsync(CancellationToken.None);

        var aliases = snapshots
            .Select(node => node.WithScope(otherScope.ToUpperInvariant()))
            .ToArray();

        context.AttachRange(aliases);
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<ScopeAliasNode>()
            .ForScope(affectedScope)
            .DeleteTreeAsync(treeId, CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.Equal(1, probe.NativeReads);
        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.All(
            aliases,
            node => Assert.Equal(
                EntityState.Unchanged,
                context.Entry(node)
                    .State));

        Assert.Equal(
            2,
            await context
                .NestedSet<ScopeAliasNode>()
                .ForScope(otherScope)
                .InTree(treeId)
                .Nodes
                .CountAsync(CancellationToken.None));

        Assert.Equal(
            0,
            await context
                .NestedSet<ScopeAliasNode>()
                .ForScope(affectedScope)
                .InTree(treeId)
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Different representations of a native Scope alias remain complete independent key groups.</summary>
    [Fact]
    public async Task DeleteTreeAsync_NativeScopeAliases_KeepBothProviderRepresentationGroups()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ScopedScopeAliasContext>(
            Engine,
            static options => new ScopedScopeAliasContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var scope = $"guard-alias-groups-{root}";
        var affectedTree = Guid.NewGuid();
        var otherTree = Guid.NewGuid();
        await NativeTrackedIdentityGuardTestSupport.SeedScopedNativeAsync(
            context,
            scope,
            affectedTree,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        await NativeTrackedIdentityGuardTestSupport.SeedScopedNativeAsync(
            context,
            scope,
            otherTree,
            root + 10,
            cancellationToken: TestContext.Current.CancellationToken);

        var snapshots = await context
            .Set<ScopeAliasNode>()
            .AsNoTracking()
            .Where(node => node.Scope == scope && node.TreeId == otherTree)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        context.Attach(snapshots[0]);
        context.Attach(
            snapshots[1]
                .WithScope(scope.ToUpperInvariant()));

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<ScopeAliasNode>()
            .ForScope(scope)
            .DeleteTreeAsync(affectedTree, CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.Equal(2, probe.NativeReads);
        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(
            2,
            context
                .ChangeTracker
                .Entries<ScopeAliasNode>()
                .Count());

        Assert.All(
            context.ChangeTracker.Entries<ScopeAliasNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));

        Assert.Equal(
            2,
            await context
                .NestedSet<ScopeAliasNode>()
                .ForScope(scope)
                .InTree(otherTree)
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Broad domain Scope equality cannot discard a provider-distinct group's duplicate NodeKey.</summary>
    [Fact]
    public async Task DeleteTreeAsync_BroadScopeEquality_KeepsBothProviderDistinctKeyGroups()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<BroadScopeContext>(
            Engine,
            static options => new BroadScopeContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var first = new BroadScope($"GUARD-GROUPS-{root}");
        var second = new BroadScope(first.Value.ToLowerInvariant());
        var affected = new BroadScope($"guard-selected-{root}");
        var treeId = Guid.NewGuid();
        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(
            context,
            first,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(
            context,
            second,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(
            context,
            affected,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        _ = await context
            .Set<BroadScopeNode>()
            .Where(node => node.Scope == first || node.Scope == second)
            .ToArrayAsync(CancellationToken.None);

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadScopeNode>()
            .ForScope(affected)
            .DeleteTreeAsync(treeId, CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.True(first.Equals(second));
        Assert.Equal(2, probe.NativeReads);
        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(
            4,
            context
                .ChangeTracker
                .Entries<BroadScopeNode>()
                .Count());

        Assert.All(
            context.ChangeTracker.Entries<BroadScopeNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));

        Assert.Equal(
            2,
            await context
                .NestedSet<BroadScopeNode>()
                .ForScope(first)
                .InTree(treeId)
                .Nodes
                .CountAsync(CancellationToken.None));

        Assert.Equal(
            2,
            await context
                .NestedSet<BroadScopeNode>()
                .ForScope(second)
                .InTree(treeId)
                .Nodes
                .CountAsync(CancellationToken.None));
    }
}
