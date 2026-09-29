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
    /// <param name="initialize">The asynchronous initialization action.</param>
    internal static async Task InitializeAsync(
        IAsyncDisposable resource,
        Func<Task> initialize
    )
    {
        try
        {
            await initialize();
        }
        catch (Exception initialization)
        {
            try
            {
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
