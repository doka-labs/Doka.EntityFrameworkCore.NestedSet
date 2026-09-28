namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Bounds scalar transport across many small provider-distinct Scope groups.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Scalar candidate batches grow with total rows rather than the number of one-node Scopes.</summary>
    /// <param name="scopes">The number of independently persisted one-node Scopes.</param>
    [Theory]
    [InlineData(768)]
    [InlineData(769)]
    public async Task TinyScalarScopesShareCombinedCandidateBatches(
        int scopes
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ConvertedBroadScopeContext>(
            Engine,
            static options => new ConvertedBroadScopeContext(options),
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var otherTree = Guid.NewGuid();
        await SeedTinyScopesAsync(context, otherTree, root, scopes);
        var selectedScope = new BroadScope($"TINY-SELECTED-{root}");
        var selectedTree = Guid.NewGuid();
        await SeedPairRootAsync(context, selectedScope, selectedTree, root + 10);
        var tracked = await context
            .Set<BroadScopeNode>()
            .Where(node => node.TreeId == otherTree)
            .ToArrayAsync(CancellationToken.None);

        var before = tracked
            .Select(PairSnapshot)
            .ToArray();

        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType,
            selectedScope,
            selectedTree,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var calls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                calls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.Equal(1, calls);
        Assert.Equal((scopes + 767) / 768, probe.NativeReads);

        // WHY: Every one-node Scope contributes its Scope and converted key, plus one requested Scope/TreeId pair.
        Assert.InRange(probe.MaximumNativeParameters, 1, (Math.Min(scopes, 768) * 2) + 2);
        Assert.Equal(scopes, tracked.Length);
        Assert.Equal(before, tracked.Select(PairSnapshot));
        Assert.All(tracked, node => Assert.Equal(EntityState.Unchanged, context.Entry(node).State));
        Assert.Equal(
            scopes,
            await context
                .Set<BroadScopeNode>()
                .AsNoTracking()
                .CountAsync(node => node.TreeId == otherTree, CancellationToken.None));

        Assert.Equal(
            1,
            await context
                .NestedSet<BroadScopeNode>()
                .ForScope(selectedScope)
                .InTree(selectedTree)
                .Nodes
                .CountAsync(CancellationToken.None));

        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>An affected stale candidate in the 769th Scope stops work after the second scalar probe.</summary>
    [Fact]
    public async Task LastTinyScalarScopeRejectsStaleMembershipInSecondCandidateBatch()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ConvertedBroadScopeContext>(
            Engine,
            static options => new ConvertedBroadScopeContext(options),
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var treeId = Guid.NewGuid();
        await SeedTinyScopesAsync(context, treeId, root, 769);
        var tracked = await context
            .Set<BroadScopeNode>()
            .Where(node => node.TreeId == treeId)
            .ToArrayAsync(CancellationToken.None);

        // WHY: The provider need not return Scopes in lexical order. Use the actual final tracker entry and
        // restore it as Unchanged with a stale TreeId, leaving its persisted complete identity unchanged.
        var last = context
            .ChangeTracker
            .Entries<BroadScopeNode>()
            .Last();

        last.State = EntityState.Detached;
        last.Entity.TreeId = Guid.NewGuid();
        context.Attach(last.Entity);
        var before = tracked
            .Select(PairSnapshot)
            .ToArray();

        var selectedScope = last.Entity.Scope;
        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType,
            selectedScope,
            treeId,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var calls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                calls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, calls);
        Assert.Equal(2, probe.NativeReads);
        Assert.InRange(probe.MaximumNativeParameters, 1, 1538);
        Assert.Equal(
            769,
            context
                .ChangeTracker
                .Entries<BroadScopeNode>()
                .Count());
        Assert.Equal(before, tracked.Select(PairSnapshot));
        Assert.All(tracked, node => Assert.Equal(EntityState.Unchanged, context.Entry(node).State));
        Assert.Equal(
            769,
            await context
                .Set<BroadScopeNode>()
                .AsNoTracking()
                .CountAsync(node => node.TreeId == treeId, CancellationToken.None));

        Assert.Equal(
            treeId,
            (await context
                .NestedSet<BroadScopeNode>()
                .ForScope(selectedScope)
                .InTree(treeId)
                .Nodes
                .SingleAsync(CancellationToken.None)).TreeId);

        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>A current TreeId does not make a domain-equal but provider-distinct Scope appear affected.</summary>
    [Fact]
    public async Task ScalarScopeDomainEqualityDoesNotRejectProviderDistinctCurrentIdentity()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ConvertedBroadScopeContext>(
            Engine,
            static options => new ConvertedBroadScopeContext(options),
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var trackedScope = new BroadScope($"SCALAR-CURRENT-{root}");
        var requestedScope = new BroadScope(trackedScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        await SeedPairRootAsync(context, trackedScope, treeId, root);
        await SeedPairRootAsync(context, requestedScope, treeId, root);
        var persisted = await context
            .Set<BroadScopeNode>()
            .AsNoTracking()
            .Where(node => node.Id == root)
            .ToArrayAsync(CancellationToken.None);

        var persistedBefore = persisted
            .OrderBy(node => node.Scope.Value, StringComparer.Ordinal)
            .Select(PairSnapshot)
            .ToArray();

        var tracked = await context
            .Set<BroadScopeNode>()
            .SingleAsync(node => node.Scope == trackedScope && node.Id == root, CancellationToken.None);

        // WHY: The tracked TreeId is current and matches the request. A broad CLR Scope equality shortcut
        // would reject before SQL even though the mapped binary comparison identifies another complete pair.
        var before = PairSnapshot(tracked);
        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType,
            requestedScope,
            treeId,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var calls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                calls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.True(trackedScope.Equals(requestedScope));
        Assert.NotEqual(trackedScope.Value, requestedScope.Value);
        Assert.Equal(1, calls);
        Assert.Equal(1, probe.NativeReads);
        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(before, PairSnapshot(tracked));
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Single(context.ChangeTracker.Entries<BroadScopeNode>());
        Assert.Equal(2, persistedBefore.Length);
        Assert.Equal(
            persistedBefore,
            (await context
                .Set<BroadScopeNode>()
                .AsNoTracking()
                .Where(node => node.Id == root)
                .ToArrayAsync(CancellationToken.None))
            .OrderBy(node => node.Scope.Value, StringComparer.Ordinal)
            .Select(PairSnapshot));

        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Domain-equal Scopes retain both stale candidate groups when their stored values differ.</summary>
    [Fact]
    public async Task ScalarScopeDomainEqualityKeepsBothStaleCandidateGroups()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ConvertedBroadScopeContext>(
            Engine,
            static options => new ConvertedBroadScopeContext(options),
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var firstScope = new BroadScope($"SCALAR-STALE-{root}");
        var secondScope = new BroadScope(firstScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        await SeedPairRootAsync(context, firstScope, treeId, root);
        await SeedPairRootAsync(context, secondScope, treeId, root);
        var tracked = await context
            .Set<BroadScopeNode>()
            .AsNoTracking()
            .Where(node => node.Id == root)
            .ToArrayAsync(CancellationToken.None);

        var persistedBefore = tracked
            .OrderBy(node => node.Scope.Value, StringComparer.Ordinal)
            .Select(PairSnapshot)
            .ToArray();

        var staleTreeId = Guid.NewGuid();
        foreach (var node in tracked)
        {
            node.TreeId = staleTreeId;
        }

        context.AttachRange(tracked);

        // WHY: The same NodeKey exists in both domain-equal Scopes. Select the last actual tracker group so
        // collapsing scalar groups by broad CLR equality cannot accidentally retain the affected first group.
        var entries = context
            .ChangeTracker
            .Entries<BroadScopeNode>()
            .ToArray();

        var requestedScope = entries[^1].Entity.Scope;
        var before = tracked
            .Select(PairSnapshot)
            .ToArray();

        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType,
            requestedScope,
            treeId,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var calls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                calls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.True(firstScope.Equals(secondScope));
        Assert.NotEqual(firstScope.Value, secondScope.Value);
        Assert.Equal(2, entries.Length);
        Assert.Equal(0, calls);
        Assert.Equal(1, probe.NativeReads);
        Assert.InRange(probe.MaximumNativeParameters, 1, 6);
        Assert.Equal(before, tracked.Select(PairSnapshot));
        Assert.Equal(
            2,
            context
                .ChangeTracker
                .Entries<BroadScopeNode>()
                .Count());
        Assert.All(tracked, node => Assert.Equal(EntityState.Unchanged, context.Entry(node).State));
        Assert.Equal(2, persistedBefore.Length);
        Assert.Equal(
            persistedBefore,
            (await context
                .Set<BroadScopeNode>()
                .AsNoTracking()
                .Where(node => node.Id == root)
                .ToArrayAsync(CancellationToken.None))
            .OrderBy(node => node.Scope.Value, StringComparer.Ordinal)
            .Select(PairSnapshot));

        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Creates actual scoped roots through the public insertion and registry contracts.</summary>
    private static async Task SeedTinyScopesAsync(
        DbContext context,
        Guid treeId,
        int root,
        int scopes
    )
    {
        for (var index = 0; index < scopes; index++)
        {
            await SeedPairRootAsync(context, new BroadScope($"TINY-{root}-{index:D4}"), treeId, root);
        }
    }
}
