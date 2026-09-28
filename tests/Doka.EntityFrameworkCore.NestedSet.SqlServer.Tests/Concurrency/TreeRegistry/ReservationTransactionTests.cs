namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Verifies SQL Server reservation failures preserve caller transactions.</summary>
public sealed class ReservationTransactionTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, SqlServerEngine>>
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the exact provider fixture owning this suite's database.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    public ReservationTransactionTests(
        ProviderFixture<RelationalFixture, SqlServerEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// A duplicate SQL Server reservation leaves earlier caller work committable with XACT_ABORT OFF.
    /// </summary>
    [Fact]
    public async Task DuplicateReservationPreservesCallerTransaction()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken.None);

        await TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            Execution.NestedSetTreeLockMode.New,
            CancellationToken.None);

        // Act
        var failure = await Record.ExceptionAsync(() => TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            Execution.NestedSetTreeLockMode.New,
            CancellationToken.None));

        var state = await context
            .Database
            .SqlQueryRaw<short>("SELECT XACT_STATE() AS [Value]")
            .SingleAsync(CancellationToken.None);

        await transaction.CommitAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, state);
        await using var verification = database.CreateContext();
        Assert.Equal(0, await TreeRegistryLockTestSupport.ReadRevisionAsync(verification, 7, treeId));
    }

    /// <summary>XACT_ABORT ON is rejected before a duplicate can invalidate the caller transaction.</summary>
    [Fact]
    public async Task XactAbortOnRejectsAndPreservesCallerTransaction()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken.None);

        await TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            Execution.NestedSetTreeLockMode.New,
            CancellationToken.None);

        await context.Database.ExecuteSqlRawAsync("SET XACT_ABORT ON", CancellationToken.None);

        // Act
        var failure = await Record.ExceptionAsync(() => TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            Execution.NestedSetTreeLockMode.New,
            CancellationToken.None));

        var state = await context
            .Database
            .SqlQueryRaw<short>("SELECT XACT_STATE() AS [Value]")
            .SingleAsync(CancellationToken.None);

        await transaction.CommitAsync(CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync("SET XACT_ABORT OFF", CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidTransaction, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, state);
        await using var verification = database.CreateContext();
        Assert.Equal(0, await TreeRegistryLockTestSupport.ReadRevisionAsync(verification, 7, treeId));
    }
}
