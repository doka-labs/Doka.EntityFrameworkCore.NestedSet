namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>An affected stale candidate in the 769th Scope stops work after the second scalar probe.</summary>
    [Fact]
    public async Task ExecuteAsync_LastTinyScalarScope_RejectsStaleMembershipInSecondCandidateBatch()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ConvertedBroadScopeContext>(
            Engine,
            static options => new ConvertedBroadScopeContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var treeId = Guid.NewGuid();
        await SeedTinyScopesAsync(context, treeId, root, 769, cancellationToken: TestContext.Current.CancellationToken);
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
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

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
        Assert.All(
            tracked,
            node => Assert.Equal(
                EntityState.Unchanged,
                context.Entry(node)
                    .State));

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

    /// <summary>Domain-equal Scopes retain both stale candidate groups when their stored values differ.</summary>
    [Fact]
    public async Task ExecuteAsync_ScalarScopeDomainEquality_KeepsBothStaleCandidateGroups()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ConvertedBroadScopeContext>(
            Engine,
            static options => new ConvertedBroadScopeContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var firstScope = new BroadScope($"SCALAR-STALE-{root}");
        var secondScope = new BroadScope(firstScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        await SeedPairRootAsync(
            context,
            firstScope,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPairRootAsync(
            context,
            secondScope,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

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
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

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

        Assert.All(
            tracked,
            node => Assert.Equal(
                EntityState.Unchanged,
                context.Entry(node)
                    .State));

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
}
