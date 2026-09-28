namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class WriteAmplificationTests
{
    /// <summary>
    /// Cancellation after an actual combined write restores every coordinate under owned or caller transactions.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationAfterCombinedWriteRestoresForest(
        bool callerTransaction,
        bool promote
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        using var cancellation = new CancellationTokenSource();
        var probe = new StructuralWriteProbe { CancelAfterBounds = cancellation };
        await using var context = database.CreateContext(probe);
        var before = await SnapshotAsync(context);
        await using var transaction = callerTransaction
            ? await context.Database.BeginTransactionAsync(
                Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;

        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => promote
            ? service.DeleteAsync(2, cancellation.Token)
            : service.MoveBeforeAsync(2, 5, cancellation.Token));

        var after = await SnapshotAsync(context);

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Single(probe.BoundsWrites);
        Assert.True(probe.BoundsWrites[0].Rows > 0);
        Assert.Equal(before, after);
        Assert.Equal(callerTransaction, context.Database.CurrentTransaction is not null);
    }

    /// <summary>
    /// Internal insertion suppresses only its redundant EF savepoint and always restores caller configuration.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertUsesOnlyTheCoordinatorSavepoint(
        bool callerTransaction
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        var observer = new InsertSavepointProbe();
        await using var context = database.CreateContext(observer);
        await using var transaction = callerTransaction
            ? await context.Database.BeginTransactionAsync(
                Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;

        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await service.InsertRootAsync(new TreeNode { NodeId = 108 }, Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Equal(callerTransaction ? 1 : 0, observer.Created);
        Assert.True(context.Database.AutoSavepointsEnabled);
    }

    /// <summary>
    /// A failed insert restores EF's caller-owned automatic savepoint setting as well as persisted rows.
    /// </summary>
    [Fact]
    public async Task FailedInsertRestoresAutomaticSavepoints()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await WriteAmplificationTestSupport.SeedAsync(database);
        await using var context = database.CreateContext();
        var before = await SnapshotAsync(context);
        var service = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => service.InsertRootAsync(
            new TreeNode { NodeId = 1 },
            Guid.NewGuid(),
            CancellationToken.None));

        var after = await SnapshotAsync(context);

        // Assert
        Assert.IsAssignableFrom<DbUpdateException>(error);
        Assert.True(context.Database.AutoSavepointsEnabled);
        Assert.Equal(before, after);
    }

    /// <summary>Transaction-only observations reuse EF services without adding unused materialization hooks.</summary>
    [Fact]
    public async Task TransactionObservationReusesFixtureServices()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var plain = database.CreateContext();
        await using var observed = database.CreateContext(new InsertSavepointProbe());

        // Act
        var ordinaryServices = plain.GetService<IModelSource>();
        var observedServices = observed.GetService<IModelSource>();

        // Assert
        Assert.Same(ordinaryServices, observedServices);
    }

    /// <summary>
    /// Captures all structure, including the overlapping isolated scope, for exact rollback comparisons.
    /// </summary>
    private static async Task<string[]> SnapshotAsync(
        TreeContext context
    )
    {
        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.NodeId)
            .ToArrayAsync(CancellationToken.None);

        return nodes
            .Select(node =>
                $"{node.NodeId}:{node.Tree}:{node.Parent}:{node.Start}:{node.End}:{node.Depth}:{node.Position}")
            .ToArray();
    }
}

/// <summary>Counts database savepoint creation independently of ordinary EF commands.</summary>
internal sealed class InsertSavepointProbe : DbTransactionInterceptor
{
    /// <summary>Gets savepoints actually created during the observed operation.</summary>
    internal int Created { get; private set; }

    /// <inheritdoc />
    public override Task CreatedSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Created++;

        return Task.CompletedTask;
    }
}
