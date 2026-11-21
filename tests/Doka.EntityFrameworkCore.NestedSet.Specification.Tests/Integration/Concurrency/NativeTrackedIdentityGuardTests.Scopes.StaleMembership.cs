namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>A provider-distinct Scope alias with the same NodeKey must not appear affected in native SQL.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ProviderDistinctScopeAlias_KeepsUnaffectedTrackedDuplicateKey(
        bool convertedKeys
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = convertedKeys
            ? await _fixture.CreateContextAsync<ConvertedBroadScopeContext>(
                Engine,
                static options => new ConvertedBroadScopeContext(options),
                cancellationToken: TestContext.Current.CancellationToken,
                probe)
            : await _fixture.CreateContextAsync<BroadScopeContext>(
                Engine,
                static options => new BroadScopeContext(options),
                cancellationToken: TestContext.Current.CancellationToken,
                probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var trackedScope = new BroadScope($"GUARD-DISTINCT-SCOPE-{root}");
        var requestedScope = new BroadScope(trackedScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(
            context,
            trackedScope,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(
            context,
            requestedScope,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        var stale = await context
            .Set<BroadScopeNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Scope == trackedScope && node.Id == root + 1, CancellationToken.None);

        // WHY: A stale TreeId forces persisted membership inspection. The same NodeKey exists in both Scopes,
        // so only the mapped binary Scope collation can distinguish the unaffected tracked row.
        stale.TreeId = Guid.NewGuid();
        context.Attach(stale);
        var before = (stale.Scope.Value, stale.Id, stale.TreeId, stale.ParentId, stale.Left, stale.Right, stale.Depth,
            stale.Position, stale.Name);

        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType,
            requestedScope,
            treeId,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var operationCalls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                operationCalls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.True(trackedScope.Equals(requestedScope));
        Assert.Equal(1, probe.NativeReads);
        Assert.Equal(
            Engine is "MySql" or "MariaDb"
                ? 1
                : 0,
            probe.NativeScopeComparisons);

        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(1, operationCalls);
        Assert.Equal(
            before,
            (stale.Scope.Value, stale.Id, stale.TreeId, stale.ParentId, stale.Left, stale.Right, stale.Depth,
                stale.Position, stale.Name));

        Assert.Single(context.ChangeTracker.Entries<BroadScopeNode>());
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(stale)
                .State);

        Assert.Equal(
            2,
            await context
                .NestedSet<BroadScopeNode>()
                .ForScope(trackedScope)
                .InTree(treeId)
                .Nodes
                .CountAsync(CancellationToken.None));

        Assert.Equal(
            2,
            await context
                .NestedSet<BroadScopeNode>()
                .ForScope(requestedScope)
                .InTree(treeId)
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>An affected stale row in the second provider-distinct Scope group prevents structural work.</summary>
    [Fact]
    public async Task ExecuteAsync_SecondProviderDistinctScopeGroup_RejectsAffectedStaleMembership()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<BroadScopeContext>(
            Engine,
            static options => new BroadScopeContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var first = new BroadScope($"GUARD-SECOND-GROUP-{root}");
        var second = new BroadScope(first.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var staleTreeId = Guid.NewGuid();
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

        var stale = await context
            .Set<BroadScopeNode>()
            .AsNoTracking()
            .Where(node => node.Id == root + 1 && (node.Scope == first || node.Scope == second))
            .ToArrayAsync(CancellationToken.None);

        foreach (var node in stale)
        {
            node.TreeId = staleTreeId;
        }

        context.AttachRange(stale);

        // WHY: EF does not promise attachment order when enumerating tracked entries. Select the last actual
        // provider-representation group so this negative always exercises membership in the second probe.
        var groups = context
            .ChangeTracker
            .Entries<BroadScopeNode>()
            .Select(entry => entry.Entity.Scope.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var affectedScope = new BroadScope(groups[^1]);
        var before = stale
            .Select(node => (node.Scope.Value, node.Id, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth,
                node.Position, node.Name))
            .ToArray();

        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType,
            affectedScope,
            treeId,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var operationCalls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                operationCalls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.True(first.Equals(second));
        Assert.Equal(2, groups.Length);
        Assert.Equal(2, probe.NativeReads);
        Assert.Equal(
            Engine is "MySql" or "MariaDb"
                ? 2
                : 0,
            probe.NativeScopeComparisons);

        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(0, operationCalls);
        Assert.Equal(
            before,
            stale.Select(node => (node.Scope.Value, node.Id, node.TreeId, node.ParentId, node.Left, node.Right,
                node.Depth, node.Position, node.Name)));

        Assert.Equal(
            2,
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
