namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies rejected hierarchy work does not begin an owned database transaction.</summary>
internal sealed class RetryBoundaryTransactionProbe : DbTransactionInterceptor
{
    /// <summary>Gets the number of transaction starts attempted through this context.</summary>
    internal int Starts { get; private set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default
    )
    {
        Starts++;

        return ValueTask.FromResult(result);
    }
}
