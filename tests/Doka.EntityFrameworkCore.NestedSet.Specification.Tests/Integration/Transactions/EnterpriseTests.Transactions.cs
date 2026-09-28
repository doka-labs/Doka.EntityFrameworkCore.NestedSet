namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>Verifies that application changes before and after a tree write share the caller's commit.</summary>
    /// <returns>A task that completes after verifying the combined persisted unit of work.</returns>
    [Fact]
    public async Task CallerCommitPersistsTreeAndApplicationChanges()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await using var transaction = await BeginCallerAsync(context);
        var preceding = new UnrelatedRow
        {
            Id = 1,
            Value = "before",
        };

        await context.AddAsync(preceding, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await tree.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        var retainedOwnership = ReferenceEquals(transaction, context.Database.CurrentTransaction);
        await context.AddAsync(
            new UnrelatedRow
            {
                Id = 2,
                Value = "after",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        // Assert
        Assert.True(retainedOwnership);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(preceding)
                .State);
        await using var verification = database.CreateContext();
        Assert.Equal(
            1,
            await verification
                .Set<TreeNode>()
                .Select(node => node.NodeId)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(
            2,
            await verification
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Verifies that caller rollback removes both tree writes and already saved application changes.</summary>
    /// <returns>A task that completes after checking the rolled-back unit of work.</returns>
    [Fact]
    public async Task CallerRollbackUndoesTreeAndApplicationChanges()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        await using var transaction = await BeginCallerAsync(context);
        await context.AddAsync(
            new UnrelatedRow
            {
                Id = 1,
                Value = "before",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await tree.InsertRootAsync(new TreeNode { NodeId = 1 }, Guid.Empty, CancellationToken.None);
        await context.AddAsync(
            new UnrelatedRow
            {
                Id = 2,
                Value = "after",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        await using var verification = database.CreateContext();
        Assert.Empty(
            await verification
                .Set<TreeNode>()
                .ToListAsync(CancellationToken.None));
        Assert.Empty(
            await verification
                .Set<UnrelatedRow>()
                .ToListAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Verifies that a failed repair rolls back to its savepoint and leaves the caller transaction usable.
    /// </summary>
    /// <returns>A task that completes after checking preserved earlier work and a successful later commit.</returns>
    [Fact]
    public async Task FailedRepairPreservesCallerWorkAndAllowsSubsequentCommit()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(129), true);
        var before = await SnapshotAsync(setup);
        var probe = new EnterpriseProbe { FailSecondNodeUpdate = true };
        await using var context = database.CreateContext((IInterceptor)probe);
        await using var transaction = await BeginCallerAsync(context);
        await context.AddAsync(
            new UnrelatedRow
            {
                Id = 1,
                Value = "before failure",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None));

        var retainedOwnership = ReferenceEquals(transaction, context.Database.CurrentTransaction);
        await context.AddAsync(
            new UnrelatedRow
            {
                Id = 2,
                Value = "after failure",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        // Assert
        Assert.IsType<InjectedCommandException>(exception);
        Assert.True(retainedOwnership);
        Assert.Equal(1, probe.CompletedNodeUpdates);
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
        Assert.Equal(
            2,
            await verification
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Verifies savepoint rollback uses a live token after the caller cancels the repair token.</summary>
    /// <returns>A task that completes after proving rollback and caller recovery from cancellation.</returns>
    [Fact]
    public async Task CanceledRepairPreservesCallerWorkAndAllowsSubsequentCommit()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(129), true);
        var before = await SnapshotAsync(setup);
        using var cancellation = new CancellationTokenSource();
        var probe = new EnterpriseProbe { CancelSecondNodeUpdate = cancellation };
        await using var context = database.CreateContext((IInterceptor)probe);
        await using var transaction = await BeginCallerAsync(context);
        await context.AddAsync(
            new UnrelatedRow
            {
                Id = 1,
                Value = "before cancellation",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => tree
            .InTree(Guid.Empty)
            .RebuildAsync(cancellation.Token));

        // WHY: Caller recovery is independent of the canceled repair; its writes and commit must still succeed.
        await context.AddAsync(
            new UnrelatedRow
            {
                Id = 2,
                Value = "after cancellation",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(1, probe.CompletedNodeUpdates);
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
        Assert.Equal(
            2,
            await verification
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Verifies that snapshot-preserving server isolation is rejected before any hierarchy command.</summary>
    /// <returns>A task that completes after verifying rejection and caller ownership.</returns>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite cannot begin the RepeatableRead caller transaction required to exercise this rejection.")]
    public async Task CallerRepeatableReadIsRejectedBeforeHierarchySql()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            CancellationToken.None);

        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            new TreeNode { NodeId = 1 },
            Guid.Empty,
            CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Empty(probe.Commands);
        Assert.Same(transaction, context.Database.CurrentTransaction);
    }

    /// <summary>Verifies that implicit ambient ownership is rejected before any hierarchy command.</summary>
    /// <returns>A task that completes after checking the ambient-transaction boundary.</returns>
    [Fact]
    public async Task AmbientTransactionIsRejectedBeforeHierarchySql()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        using var ambient = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

        // Act
        var exception = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            new TreeNode { NodeId = 1 },
            Guid.Empty,
            CancellationToken.None));

        // Assert
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Empty(probe.Commands);
    }
}
