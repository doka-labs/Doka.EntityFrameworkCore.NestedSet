namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Retains EF's actual retry boundary for unrelated writes even when the model contains ordered nodes.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnrelatedSaveRetriesWithoutHierarchyLocksAndHonorsAcceptance(
        bool acceptChanges
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var probe = new SaveBoundaryProbe { FailNextPayload = true };
        await using var services = RetryBoundaryServices.Create(Engine);
        var options = SaveBoundaryTestSupport
            .Options(setup, probe)
            .UseInternalServiceProvider(services);

        await using var context = new SaveBoundaryContext(options.Options);
        var marker = new OrderingMarker
        {
            Id = 1,
            Value = "retry payload",
        };

        await context.AddAsync(marker, CancellationToken.None);

        // Act
        var saved = await context.SaveChangesAsync(acceptChanges, CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Equal(2, probe.PayloadCommands);
        Assert.Equal(0, probe.LockCommands);
        Assert.Equal(
            acceptChanges ? EntityState.Unchanged : EntityState.Added,
            context.Entry(marker).State);
        Assert.Equal(
            "retry payload",
            await setup
                .Set<OrderingMarker>()
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Leaves transaction isolation and completion under caller control for ordinary payload writes.</summary>
    [Theory]
    [InlineData(IsolationLevel.Serializable)]
    [EngineInlineData(
        IsolationLevel.RepeatableRead,
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite exposes Serializable and ReadUncommitted transactions, not RepeatableRead.")]
    public async Task UnrelatedSavePreservesCallerTransactionIsolation(
        IsolationLevel isolation
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var probe = new SaveBoundaryProbe();
        await using var context = new SaveBoundaryContext(SaveBoundaryTestSupport.Options(setup, probe).Options);
        await using var transaction = await context.Database.BeginTransactionAsync(isolation, CancellationToken.None);
        await context.AddAsync(
            new OrderingMarker
            {
                Id = 1,
                Value = "caller-owned",
            },
            CancellationToken.None);

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);
        var current = context.Database.CurrentTransaction;
        var actualIsolation = transaction.GetDbTransaction().IsolationLevel;

        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, saved);
        Assert.Same(transaction, current);
        Assert.Equal(isolation, actualIsolation);
        Assert.Equal(0, probe.LockCommands);
        Assert.Empty(
            await setup
                .Set<OrderingMarker>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>
    ///     Does not restore a pre-save snapshot over ordinary callback changes when a native command fails.
    /// </summary>
    [Fact]
    public async Task FailedUnrelatedSaveKeepsNativePendingCallbackState()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var probe = new SaveBoundaryProbe { FailNextPayload = true };
        var callback = new SaveBoundaryCallback();
        await using var context = new SaveBoundaryContext(
            SaveBoundaryTestSupport.Options(setup, probe, callback).Options);

        var marker = new OrderingMarker
        {
            Id = 1,
            Value = "before callback",
        };

        await context.AddAsync(marker, CancellationToken.None);
        callback.OnSaving = _ => marker.Value = "from callback";

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        Assert.NotNull(failure);
        Assert.Equal("from callback", marker.Value);
        Assert.Equal(EntityState.Added, context.Entry(marker).State);
        Assert.Equal(0, probe.LockCommands);
        Assert.Empty(
            await setup
                .Set<OrderingMarker>()
                .ToArrayAsync(CancellationToken.None));
    }
}
