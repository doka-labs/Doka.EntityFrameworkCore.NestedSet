namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies benchmark startup ownership before a container reaches its caller's disposal scope.</summary>
public sealed class ResourceInitializationTests
{
    /// <summary>Successful initialization transfers the still-live resource to its caller.</summary>
    /// <param name="cancellable">Whether initialization receives a live token or explicit None.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulInitializationKeepsCallerOwnership(
        bool cancellable
    )
    {
        // Arrange
        await using var resource = new StartupResource();
        using var cancellation = new CancellationTokenSource();
        var token = cancellable ? cancellation.Token : CancellationToken.None;
        var initialized = false;
        var observed = CancellationToken.None;

        // Act
        await BenchmarkLifecycle.InitializeAsync(
            resource,
            received =>
            {
                observed = received;
                initialized = true;

                return Task.CompletedTask;
            }, token);

        // Assert
        Assert.True(initialized);
        Assert.Equal(token, observed);
        Assert.Equal(0, resource.DisposeCalls);
    }

    /// <summary>A startup failure releases the owned resource and preserves the exact original error.</summary>
    [Fact]
    public async Task FailedInitializationDisposesTheOwnedResource()
    {
        // Arrange
        var resource = new StartupResource();
        var original = new InvalidOperationException("Injected startup failure.");

        // Act
        var error = await Record.ExceptionAsync(() => BenchmarkLifecycle.InitializeAsync(
            resource,
            _ => Task.FromException(original), cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(original, error);
        Assert.Equal(1, resource.DisposeCalls);
    }

    /// <summary>Cancellation during startup still disposes the resource without replacing cancellation.</summary>
    [Fact]
    public async Task CanceledInitializationPreservesCancellationAndDisposes()
    {
        // Arrange
        var resource = new StartupResource();
        var original = new OperationCanceledException(new CancellationToken(true));

        // Act
        var error = await Record.ExceptionAsync(() => BenchmarkLifecycle.InitializeAsync(
            resource,
            _ => Task.FromException(original), cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(original, error);
        Assert.Equal(1, resource.DisposeCalls);
    }

    /// <summary>A failing cleanup retains both failures rather than hiding the initialization error.</summary>
    [Fact]
    public async Task FailedCleanupPreservesBothErrors()
    {
        // Arrange
        var cleanup = new IOException("Injected cleanup failure.");
        var resource = new StartupResource(cleanup);
        var original = new InvalidOperationException("Injected startup failure.");

        // Act
        var error = await Record.ExceptionAsync(() => BenchmarkLifecycle.InitializeAsync(
            resource,
            _ => Task.FromException(original), cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        var aggregate = Assert.IsType<AggregateException>(error);
        Assert.Equal([original, cleanup], aggregate.InnerExceptions);
        Assert.Equal(1, resource.DisposeCalls);
    }

    /// <summary>Pre-cancellation skips startup but still releases the not-yet-transferred resource.</summary>
    [Fact]
    public async Task PreCanceledInitializationSkipsActionAndReleasesResource()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var resource = new StartupResource();
        var calls = 0;

        // Act
        var error = await Record.ExceptionAsync(() => BenchmarkLifecycle.InitializeAsync(
            resource,
            _ =>
            {
                calls++;

                return Task.CompletedTask;
            },
            cancellation.Token));

        // Assert
        var canceled = Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, calls);
        Assert.Equal(1, resource.DisposeCalls);
    }

    /// <summary>An interrupted startup waits for token-free cleanup before exposing its cancellation.</summary>
    [Fact]
    public async Task CanceledInitializationWaitsForUninterruptibleCleanup()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var resource = new DeferredStartupResource();
        var observed = CancellationToken.None;
        var awaitedCleanup = false;

        // Act
        var initialization = BenchmarkLifecycle.InitializeAsync(
            resource,
            token =>
            {
                observed = token;
                cancellation.Cancel();

                return Task.FromCanceled(token);
            },
            cancellation.Token);

        try
        {
            await resource.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            awaitedCleanup = !initialization.IsCompleted;
        }
        finally
        {
            resource.Release.TrySetResult();
        }

        var error = await Record.ExceptionAsync(() => initialization);

        // Assert
        var canceled = Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Equal(cancellation.Token, observed);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(awaitedCleanup);
        Assert.Equal(1, resource.DisposeCalls);
    }

    /// <summary>Exposes the actual awaited cleanup boundary without a timing-dependent delay.</summary>
    private sealed class DeferredStartupResource : IAsyncDisposable
    {
        /// <summary>Signals that unconditional cleanup has reached its deliberate asynchronous boundary.</summary>
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Allows the test to release cleanup independently of the canceled initialization token.</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets the exact number of cleanup attempts.</summary>
        internal int DisposeCalls { get; private set; }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Started.SetResult();

            // WHY: Cleanup remains pending independently of the token that already canceled initialization.
            await Release.Task;
        }
    }

    /// <summary>Represents only the owned disposal boundary; no container or Docker mock is required.</summary>
    private sealed class StartupResource(Exception? cleanup = null) : IAsyncDisposable
    {
        /// <summary>Gets how many times startup attempted to release the owned resource.</summary>
        public int DisposeCalls { get; private set; }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;

            return cleanup is null ? ValueTask.CompletedTask : ValueTask.FromException(cleanup);
        }
    }
}
