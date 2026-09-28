namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Pauses SQLite's writer-reservation boundary inside the measured acquisition.</summary>
internal sealed class LockCancellationProbe : DbTransactionInterceptor
{
    private readonly TaskCompletionSource _reached;

    /// <summary>Creates an observer for the SQLite transaction boundary.</summary>
    /// <param name="reached">The signal owned by the test waiting for the measured acquisition boundary.</param>
    internal LockCancellationProbe(
        TaskCompletionSource reached
    )
    {
        _reached = reached;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default
    )
    {
        _reached.TrySetResult();

        // WHY: SQLite reserves the writer during BEGIN IMMEDIATE, before the infrastructure lock command.
        // Canceling only after this signal proves the measured boundary includes transaction acquisition.
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        return result;
    }
}
