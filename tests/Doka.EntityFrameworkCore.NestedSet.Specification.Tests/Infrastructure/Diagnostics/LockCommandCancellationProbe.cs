namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Pauses the measured server lock acquisition without claiming server contention.</summary>
internal sealed class LockCommandCancellationProbe : DbCommandInterceptor
{
    private readonly TaskCompletionSource _reached;

    /// <summary>Creates an observer for a server's infrastructure lock command.</summary>
    /// <param name="reached">The signal owned by the test waiting for the measured acquisition boundary.</param>
    internal LockCommandCancellationProbe(
        TaskCompletionSource reached
    )
    {
        _reached = reached;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (NestedSetTestInfrastructure.ReferencesRegistry(eventData.Context, command.CommandText))
        {
            _reached.TrySetResult();

            // WHY: Server engines acquire their row lock after BEGIN; transaction cancellation misses that metric.
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return result;
    }
}
