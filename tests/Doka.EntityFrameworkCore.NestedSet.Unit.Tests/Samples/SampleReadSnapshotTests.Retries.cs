extern alias UserGroupsSample;

using Microsoft.EntityFrameworkCore.Storage;
using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>A transient failure replays all participating reads in a new owned native snapshot.</summary>
    /// <returns>A task completing after the actual configured EF execution strategy has retried once.</returns>
    [Fact]
    public async Task TransientFailureReplaysTheCompleteReadInsideANewOwnedSnapshot()
    {
        // Arrange
        var observer = new SnapshotTransactionObserver();
        var options = new DbContextOptionsBuilder<SampleLifecycleContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(observer);

        // WHY: This deliberate strategy replacement belongs to one test. Own its complete EF service graph
        // instead of adding a distinct replacement to the process-wide internal provider cache.
        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        services.AddScoped<IExecutionStrategyFactory, SnapshotRetryingStrategyFactory>();
        await using var provider = services.BuildServiceProvider();
        await using var context = new SampleLifecycleContext(options.UseInternalServiceProvider(provider).Options);

        // WHY: The caller holds the in-memory database open independently of each owned read transaction.
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        context.Sentinels.Add(new SampleLifecycleSentinel { Id = 1, Value = "persisted" });
        await context.SaveChangesAsync(CancellationToken.None);
        observer.Reset();
        var attempts = new List<SqliteTransaction>();
        var reads = 0;

        // Act
        var result = await SampleReadSnapshot.RunAsync(context, async token =>
        {
            attempts.Add((SqliteTransaction)context.Database.CurrentTransaction!.GetDbTransaction());
            var first = await context.Sentinels.AsNoTracking().Select(row => row.Value).SingleAsync(token);
            reads++;

            if (attempts.Count == 1)
            {
                throw new SnapshotTransientException();
            }

            var second = await context.Sentinels.AsNoTracking().Select(row => row.Value).SingleAsync(token);
            reads++;

            return (First: first, Second: second);
        }, CancellationToken.None);

        var connectionState = context.Database.GetDbConnection().State;
        await context.Database.CloseConnectionAsync();

        // Assert
        Assert.Equal(("persisted", "persisted"), result);
        Assert.Equal(3, reads);
        Assert.Equal(2, attempts.Count);
        Assert.NotSame(attempts[0], attempts[1]);
        Assert.All(attempts, transaction => Assert.Null(transaction.Connection));
        Assert.Equal(0, observer.Started);
        Assert.Equal(2, observer.Attached);
        Assert.Equal(1, observer.Committed);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, connectionState);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Installs one narrowly defined transient retry through EF's public strategy registration.</summary>
    private sealed class SnapshotRetryingStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        /// <inheritdoc />
        public IExecutionStrategy Create() => new SnapshotRetryingStrategy(dependencies);
    }

    /// <summary>Retries only the deliberate first-read failure, without timing or arbitrary retries in tests.</summary>
    private sealed class SnapshotRetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        /// <inheritdoc />
        protected override bool ShouldRetryOn(Exception exception) => exception is SnapshotTransientException;
    }

    /// <summary>Marks the single deliberate transient read failure recognized by the test's EF strategy.</summary>
    private sealed class SnapshotTransientException : Exception;
}
