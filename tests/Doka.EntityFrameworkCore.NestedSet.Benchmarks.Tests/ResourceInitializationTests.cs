namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies benchmark startup ownership before a container reaches its caller's disposal scope.</summary>
public sealed class ResourceInitializationTests
{
    /// <summary>Successful initialization transfers the still-live resource to its caller.</summary>
    [Fact]
    public async Task SuccessfulInitializationKeepsCallerOwnership()
    {
        // Arrange
        await using var resource = new StartupResource();
        var initialized = false;

        // Act
        await BenchmarkLifecycle.InitializeAsync(
            resource,
            () =>
            {
                initialized = true;

                return Task.CompletedTask;
            });

        // Assert
        Assert.True(initialized);
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
            () => Task.FromException(original)));

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
            () => Task.FromException(original)));

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
            () => Task.FromException(original)));

        // Assert
        var aggregate = Assert.IsType<AggregateException>(error);
        Assert.Equal([original, cleanup], aggregate.InnerExceptions);
        Assert.Equal(1, resource.DisposeCalls);
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
