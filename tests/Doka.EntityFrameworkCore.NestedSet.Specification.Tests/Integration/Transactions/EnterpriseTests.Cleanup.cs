namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>
    ///     Verifies that a savepoint cleanup failure reports both errors and leaves disposal to the caller.
    /// </summary>
    /// <returns>A task that completes after verifying both failures and the caller's final rollback.</returns>
    [Fact]
    public async Task SavepointRollbackFailurePreservesBothExceptions()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(129), true);
        var before = await SnapshotAsync(setup);
        var commands = new EnterpriseProbe { FailSecondNodeUpdate = true };
        var cleanup = new EnterpriseTransactionProbe();
        await using var context = database.CreateContext(commands, cleanup);
        await using var transaction = await BeginCallerAsync(context);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None));

        var retainedOwnership = ReferenceEquals(transaction, context.Database.CurrentTransaction);
        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        var aggregate = Assert.IsType<AggregateException>(exception);
        Assert.Contains(aggregate.InnerExceptions, failure => failure is InjectedCommandException);
        Assert.Contains(aggregate.InnerExceptions, failure => failure is InjectedCleanupException);
        Assert.True(retainedOwnership);
        Assert.Equal(1, commands.CompletedNodeUpdates);
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
    }

    /// <summary>Verifies that owned-transaction rollback failure does not hide the operation error.</summary>
    /// <returns>A task that completes after checking aggregated failures and disposal rollback.</returns>
    [Fact]
    public async Task OwnedRollbackFailurePreservesBothExceptionsAndDisposesTransaction()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(129), true);
        var before = await SnapshotAsync(setup);
        var commands = new EnterpriseProbe { FailSecondNodeUpdate = true };
        var cleanup = new EnterpriseTransactionProbe { FailOwnedRollback = true };
        await using var context = database.CreateContext(commands, cleanup);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None));

        // Assert
        var aggregate = Assert.IsType<AggregateException>(exception);
        Assert.Contains(aggregate.InnerExceptions, failure => failure is InjectedCommandException);
        Assert.Contains(aggregate.InnerExceptions, failure => failure is InjectedCleanupException);
        Assert.Equal(1, commands.CompletedNodeUpdates);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
    }
}
