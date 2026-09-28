namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Keeps requested identities as complete Scope and TreeId pairs rather than independent value sets.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>A crossed tracked pair remains unaffected when Scope and TreeId occur in separate requests.</summary>
    [Fact]
    public async Task CrossedRequestScopeAndTreeIdDoNotFormAnAffectedPair()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<BroadScopeContext>(
            Engine,
            static options => new BroadScopeContext(options),
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var firstScope = new BroadScope($"PAIR-A-{root}");
        var secondScope = new BroadScope($"PAIR-B-{root}");
        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        await SeedPairRootAsync(context, firstScope, firstTree, root);
        await SeedPairRootAsync(context, secondScope, secondTree, root + 10);
        await SeedPairRootAsync(context, firstScope, secondTree, root + 20);
        var tracked = await context
            .Set<BroadScopeNode>()
            .SingleAsync(node => node.Scope == firstScope && node.Id == root + 20, CancellationToken.None);

        var before = PairSnapshot(tracked);
        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var requests = new[]
        {
            new NestedSetTreeLockRequest<Guid, BroadScope>(
                entityType,
                firstScope,
                firstTree,
                NestedSetTreeLockMode.Existing),
            new NestedSetTreeLockRequest<Guid, BroadScope>(
                entityType,
                secondScope,
                secondTree,
                NestedSetTreeLockMode.Existing),
        };

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
            requests,
            CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.Equal(1, calls);
        Assert.Equal(1, probe.NativeReads);
        Assert.InRange(probe.MaximumNativeParameters, 1, 6);
        Assert.Equal(before, PairSnapshot(tracked));
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Single(context.ChangeTracker.Entries<BroadScopeNode>());
        Assert.Equal(
            before,
            PairSnapshot(
                await context
                    .NestedSet<BroadScopeNode>()
                    .ForScope(firstScope)
                    .InTree(secondTree)
                    .Nodes
                    .SingleAsync(CancellationToken.None)));

        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>An exact requested pair rejects its tracked node before any SQL or operation callback.</summary>
    [Fact]
    public async Task ExactCompleteRequestPairRejectsBeforeSql()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<BroadScopeContext>(
            Engine,
            static options => new BroadScopeContext(options),
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var firstScope = new BroadScope($"PAIR-EXACT-A-{root}");
        var secondScope = new BroadScope($"PAIR-EXACT-B-{root}");
        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        await SeedPairRootAsync(context, firstScope, firstTree, root);
        await SeedPairRootAsync(context, secondScope, secondTree, root + 10);
        var tracked = await context
            .Set<BroadScopeNode>()
            .SingleAsync(node => node.Scope == firstScope && node.Id == root, CancellationToken.None);

        var before = PairSnapshot(tracked);
        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var requests = new[]
        {
            new NestedSetTreeLockRequest<Guid, BroadScope>(
                entityType,
                firstScope,
                firstTree,
                NestedSetTreeLockMode.Existing),
            new NestedSetTreeLockRequest<Guid, BroadScope>(
                entityType,
                secondScope,
                secondTree,
                NestedSetTreeLockMode.Existing),
        };

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
            requests,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, calls);
        Assert.Equal(0, probe.Commands);
        Assert.Equal(before, PairSnapshot(tracked));
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Single(context.ChangeTracker.Entries<BroadScopeNode>());
        Assert.Equal(
            before,
            PairSnapshot(
                await context
                    .NestedSet<BroadScopeNode>()
                    .ForScope(firstScope)
                    .InTree(firstTree)
                    .Nodes
                    .SingleAsync(CancellationToken.None)));

        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Seeds a real root through the public API before guard-only operation measurements.</summary>
    private static async Task SeedPairRootAsync(
        DbContext context,
        BroadScope scope,
        Guid treeId,
        int key
    )
    {
        await context
            .NestedSet<BroadScopeNode>()
            .ForScope(scope)
            .InsertRootAsync(
                new BroadScopeNode
                {
                    Id = key,
                    Name = "Root",
                },
                treeId,
                CancellationToken.None);

        context.ChangeTracker.Clear();
    }

    /// <summary>Snapshots exact Scope text, complete structure and payload independently of broad equality.</summary>
    private static (string, int, Guid, int?, long, long, int, long, string) PairSnapshot(
        BroadScopeNode node
    ) => (node.Scope.Value, node.Id, node.TreeId, node.ParentId, node.Left, node.Right, node.Depth, node.Position,
        node.Name);
}
