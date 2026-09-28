namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the Doka family's native Scope identity regressions on MySQL, including its NO PAD table collation.</summary>
[Collection("Model compatibility")]
public sealed class NativeCollationTests : NativeCollationTestBase
{
    /// <summary>Uses the exact engine fixture registered for this provider suite.</summary>
    /// <param name="fixture">The isolated provider resource from the suite or its collection.</param>
    public NativeCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }

    /// <summary>An inherited NO PAD collation preserves distinct trailing-space Scope identities.</summary>
    /// <param name="convertedKeys">Whether NodeKey transport uses the bounded converted-key fallback.</param>
    /// <returns>A task completing after preservation of the native trailing-space identity.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InheritedNoPadScopeCollationKeepsUnaffectedTrailingSpaceKey(bool convertedKeys)
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateInheritedScopeContextAsync(Engine, convertedKeys, true, probe);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var trackedScope = new BroadScope($"guard-no-pad-{root}");
        var requestedScope = new BroadScope(trackedScope.Value + " ");
        var trackedTree = Guid.NewGuid();
        var requestedTree = Guid.NewGuid();
        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(context, trackedScope, trackedTree, root);
        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(context, requestedScope, requestedTree, root);
        var tracked = await context.Set<BroadScopeNode>().AsNoTracking()
            .SingleAsync(node => node.Scope == trackedScope && node.Id == root + 1, CancellationToken.None);

        context.Attach(tracked);
        var before = (tracked.Scope.Value, tracked.Id, tracked.TreeId, tracked.ParentId,
            tracked.Left, tracked.Right, tracked.Depth, tracked.Position, tracked.Name);

        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType, requestedScope, requestedTree, NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var operationCalls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(_ =>
        {
            operationCalls++;

            return Task.CompletedTask;
        }, [request], CancellationToken.None));

        // Assert
        Assert.Null(failure);
        AssertInheritedTableCollation(context, entityType, "utf8mb4_0900_bin");
        Assert.Equal(1, probe.NativeReads);
        Assert.Equal(1, probe.NativeScopeComparisons);
        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(1, operationCalls);
        Assert.Equal(before, (tracked.Scope.Value, tracked.Id, tracked.TreeId, tracked.ParentId,
            tracked.Left, tracked.Right, tracked.Depth, tracked.Position, tracked.Name));
        Assert.Single(context.ChangeTracker.Entries<BroadScopeNode>());
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal(2, await context.NestedSet<BroadScopeNode>().ForScope(trackedScope).InTree(trackedTree).Nodes
            .CountAsync(CancellationToken.None));
        Assert.Equal(2, await context.NestedSet<BroadScopeNode>().ForScope(requestedScope).InTree(requestedTree).Nodes
            .CountAsync(CancellationToken.None));
    }
}
