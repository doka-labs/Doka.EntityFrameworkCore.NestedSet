namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves caller transaction ownership when a native tracked-identity probe is canceled.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Rolls back only the mutation savepoint and retains earlier application payload writes.</summary>
    [Fact]
    public async Task CanceledNativeProbePreservesCallerWritesAndCleanTracker()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affectedTree = $"guard-cancel-{root}";
        var otherTree = $"guard-cancel-other-{root}";
        await SeedTreeAsync(context, affectedTree, root, 1);
        await SeedTreeAsync(context, otherTree, root + 10, 1);
        await using var transaction = await context.Database.BeginTransactionAsync(
            NestedSetProviderCapabilities.Resolve(context)
                .RequiredIsolation,
            CancellationToken.None);

        var payload = await context
            .Set<BroadTreeIdNode>()
            .SingleAsync(node => node.Id == root + 10, CancellationToken.None);

        payload.Name = "Caller application write";
        await context.SaveChangesAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        probe.BeforeNativeReadAsync = async _ => await stop
            .CancelAsync()
            .ConfigureAwait(false);

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(affectedTree), stop.Token));

        // Assert
        Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(failure).CancellationToken);
        Assert.Equal(1, probe.NativeReads);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Equal(EntityState.Unchanged, context.Entry(payload).State);
        Assert.Equal("Caller application write", (await ReadNodeAsync(context, root + 10)).Name);
        Assert.Equal(
            2,
            await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(affectedTree))
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>A new token can retry after cancellation without clearing or repairing the caller tracker.</summary>
    [Fact]
    public async Task NativeProbeCanRetryAfterCancellationInTheSameCallerTransaction()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affectedTree = $"guard-retry-{root}";
        var otherTree = $"guard-retry-other-{root}";
        await SeedTreeAsync(context, affectedTree, root, 1);
        await SeedTreeAsync(context, otherTree, root + 10, 1);
        await using var transaction = await context.Database.BeginTransactionAsync(
            NestedSetProviderCapabilities.Resolve(context).RequiredIsolation,
            CancellationToken.None);

        var payload = await context
            .Set<BroadTreeIdNode>()
            .SingleAsync(node => node.Id == root + 10, CancellationToken.None);

        payload.Name = "Caller application write";
        await context.SaveChangesAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        probe.BeforeNativeReadAsync = async _ => await stop
            .CancelAsync()
            .ConfigureAwait(false);

        probe.Armed = true;
        var canceled = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(affectedTree), stop.Token));

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(affectedTree), CancellationToken.None));

        // Assert
        Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(canceled).CancellationToken);
        Assert.Null(failure);
        Assert.Equal(2, probe.NativeReads);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Equal(EntityState.Unchanged, context.Entry(payload).State);
        Assert.Equal("Caller application write", (await ReadNodeAsync(context, root + 10)).Name);
        Assert.Equal(
            0,
            await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(affectedTree))
                .Nodes
                .CountAsync(CancellationToken.None));
    }
}
