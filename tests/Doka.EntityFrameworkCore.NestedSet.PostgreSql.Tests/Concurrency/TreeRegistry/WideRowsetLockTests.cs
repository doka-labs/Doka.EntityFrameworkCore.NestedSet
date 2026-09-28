namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Verifies PostgreSQL registry lock ordering for wide request rowsets.</summary>
public sealed class WideRowsetLockTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, PostgreSqlEngine>>
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the exact provider fixture owning this suite's database.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    public WideRowsetLockTests(
        ProviderFixture<RelationalFixture, PostgreSqlEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Opposing wide request orders serialize on the same database-sorted registry sequence.</summary>
    [Fact]
    public async Task OppositeWideRequestsCompleteWithoutDeadlock()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeIds = Enumerable.Range(0, 256).Select(_ => Guid.NewGuid()).ToArray();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireManyAsync(setup, treeIds
                .Select(treeId => TreeRegistryLockTestSupport.Request(
                    setup, 7, treeId, Execution.NestedSetTreeLockMode.New))
                .ToArray(), CancellationToken.None);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ascending = TreeRegistryLockTestSupport.LockAfterGateAsync(database, 7, treeIds, gate.Task, timeout.Token);
        var descending = TreeRegistryLockTestSupport.LockAfterGateAsync(
            database, 7, treeIds.Reverse().ToArray(), gate.Task,
            timeout.Token);

        // Act
        gate.SetResult();
        await Task.WhenAll(ascending, descending);

        // Assert
        Assert.False(timeout.IsCancellationRequested);
    }
}
