namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Contains the framework-required synchronous iteration seam and owned startup failure boundary.</summary>
internal static class BenchmarkLifecycle
{
    /// <summary>Waits until asynchronous iteration preparation has completed before BDN starts measurement.</summary>
    /// <param name="action">The fully asynchronous reset or verification action.</param>
    internal static void Complete(
        Task action
    )
    {
        // ReSharper disable once AsyncMethodWithSynchronousTaskResult
        // WHY: BDN iteration hooks lack the global hooks' AwaitHelper; Task-returning methods cannot bind to Action.
        action
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Transfers ownership on success and releases partially initialized resources on failure.</summary>
    /// <param name="resource">The resource whose ownership is transferred only after initialization.</param>
    /// <param name="initialize">The asynchronous action receiving the caller's token.</param>
    /// <param name="cancellationToken">Cancellation for the operation's preparation or verification work.</param>
    internal static async Task InitializeAsync(
        IAsyncDisposable resource,
        Func<CancellationToken, Task> initialize,
        CancellationToken cancellationToken
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await initialize(cancellationToken);
        }
        catch (Exception initialization)
        {
            try
            {
                // WHY: Ownership has not transferred; cancellation must never skip releasing the partial resource.
                await resource.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(initialization, cleanup);
            }

            throw;
        }
    }
}
