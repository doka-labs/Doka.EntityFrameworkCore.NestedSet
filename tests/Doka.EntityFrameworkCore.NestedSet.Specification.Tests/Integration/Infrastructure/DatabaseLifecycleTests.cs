namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies owned fixture resources and error identity when initialization cannot return a database.</summary>
public abstract class DatabaseLifecycleTests : ProviderTest
{
    /// <summary>Uses the concrete provider fixture as the immutable engine owner.</summary>
    /// <param name="fixture">The fixture bound to this provider suite.</param>
    protected DatabaseLifecycleTests(
        IProviderFixture<ProviderResources> fixture
    ) : base(fixture) { }

    /// <summary>Resolves the executable assembly's server owner from a method in the shared library.</summary>
    [DatabaseIndependent]
    [Fact]
    public async Task SharedLibraryResolvesExecutingAssemblyFixture()
    {
        // Arrange
        var expected = await TestContext.Current.GetFixture<TestDatabaseServers>();

        // Act
        var actual = await TestDatabaseServers.GetCurrentAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(expected);
        Assert.Same(expected, actual);
        Assert.NotEqual(typeof(TestDatabaseServers).Assembly, GetType().Assembly);
        Assert.True(ProviderEngineOwnership.Includes(Engine));
    }

    /// <summary>Rejects a foreign engine before the shared fixture can start its container.</summary>
    [DatabaseIndependent]
    [Fact]
    public async Task AssemblyFixtureRejectsForeignServer()
    {
        // Arrange
        var foreignEngine = Engine is "MySql" or "MariaDb" ? "PostgreSql" : "MySql";

        var fixture = await TestDatabaseServers.GetCurrentAsync(TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() => fixture.NewConnectionStringAsync(
            foreignEngine,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.True(ProviderEngineOwnership.Includes(Engine));
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(foreignEngine, error.Message, StringComparison.Ordinal);
    }

    /// <summary>A canceled caller cannot start or cache an assembly-owned server.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite owns files rather than an assembly-shared server startup.")]
    public async Task PreCanceledServerAcquisitionNeverStartsAContainer()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var starts = 0;
        await using var fixture = new TestDatabaseServers((_, token) =>
        {
            starts++;

            return Task.FromCanceled<TestDatabaseServers.Server>(token);
        });

        // Act
        var failure = await Record.ExceptionAsync(() => fixture.NewConnectionStringAsync(Engine, cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, starts);
    }

    /// <summary>A live waiter retries only after the canceled startup owner's attempt has completed.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite owns files rather than an assembly-shared server startup.")]
    public async Task CanceledSharedStartupDoesNotPoisonALiveWaiter()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new ServerResource();
        var starts = 0;
        await using var fixture = new TestDatabaseServers(async (_, token) =>
        {
            if (Interlocked.Increment(ref starts) == 1)
            {
                entered.SetResult();
                await pending.Task.WaitAsync(token);
            }

            return new TestDatabaseServers.Server(
                resource,
                "Server=localhost;Database=nestedset;User ID=test;Password=test");
        });

        var first = fixture.NewConnectionStringAsync(Engine, cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = fixture.NewConnectionStringAsync(Engine, TestContext.Current.CancellationToken);

        // Act
        await cancellation.CancelAsync();
        var firstFailure = await Record.ExceptionAsync(() => first);
        var secondConnection = await second;

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(firstFailure);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(2, starts);
        Assert.False(string.IsNullOrWhiteSpace(secondConnection));
        Assert.False(resource.IsDisposed);
    }

    /// <summary>An unrelated provider cancellation is preserved rather than retried as caller cancellation.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite owns files rather than an assembly-shared server startup.")]
    public async Task UnrelatedStartupCancellationIsNotRetried()
    {
        // Arrange
        var expected = new OperationCanceledException("Unrelated startup timeout.", CancellationToken.None);
        var starts = 0;
        await using var fixture = new TestDatabaseServers((_, _) =>
        {
            starts++;

            return Task.FromException<TestDatabaseServers.Server>(expected);
        });

        // Act
        var failure = await Record.ExceptionAsync(() => fixture.NewConnectionStringAsync(
            Engine,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(expected, failure);
        Assert.Equal(1, starts);
    }

    /// <summary>Teardown waits for a pending startup and disposes its result despite caller cancellation.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite owns files rather than an assembly-shared server startup.")]
    public async Task ServerTeardownObservesPendingStartupAfterCallerCancellation()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var startup = new TaskCompletionSource<TestDatabaseServers.Server>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var resource = new ServerResource();
        await using var fixture = new TestDatabaseServers((_, _) => startup.Task);
        var acquisition = fixture.NewConnectionStringAsync(Engine, cancellation.Token);
        var cleanup = fixture.DisposeAsync().AsTask();
        var completedBeforeStartup = cleanup.IsCompleted;

        // Act
        await cancellation.CancelAsync();
        startup.SetResult(new TestDatabaseServers.Server(
            resource,
            "Server=localhost;Database=nestedset;User ID=test;Password=test"));
        var failure = await Record.ExceptionAsync(() => acquisition);
        await cleanup;

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.False(completedBeforeStartup);
        Assert.True(resource.IsDisposed);
    }

    /// <summary>Concurrent fixtures share the engine but never observe each other's rows.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "This lifecycle assertion compares two server database names sharing one server; "
            + "SQLite uses isolated files.")]
    public async Task ServerDatabasesShareEngineWithoutSharingRows()
    {
        // Arrange
        await using var first = await TestDatabase.CreateAsync(Engine, TestContext.Current.CancellationToken);
        await using var second = await TestDatabase.CreateAsync(Engine, TestContext.Current.CancellationToken);
        await using var firstContext = first.CreateContext();
        await using var secondContext = second.CreateContext();
        await firstContext
            .Set<UnrelatedRow>()
            .AddAsync(
                new UnrelatedRow
                {
                    Id = 1,
                    Value = "first",
                },
                CancellationToken.None);

        // Act
        await firstContext.SaveChangesAsync(CancellationToken.None);
        var secondCount = await secondContext
            .Set<UnrelatedRow>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            firstContext.Database.GetDbConnection().DataSource,
            secondContext.Database.GetDbConnection().DataSource);
        Assert.NotEqual(
            firstContext.Database.GetDbConnection().Database,
            secondContext.Database.GetDbConnection().Database);
        Assert.Equal(0, secondCount);
    }

    /// <summary>Observes fixture teardown without creating or contacting a native server container.</summary>
    private sealed class ServerResource : IAsyncDisposable
    {
        /// <summary>Gets whether terminal fixture cleanup released the startup result.</summary>
        internal bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            IsDisposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
