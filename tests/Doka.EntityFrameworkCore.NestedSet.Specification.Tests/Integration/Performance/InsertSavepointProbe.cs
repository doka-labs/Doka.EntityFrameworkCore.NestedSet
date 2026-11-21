namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Counts database savepoint creation independently of ordinary EF commands.</summary>
internal sealed class InsertSavepointProbe : DbTransactionInterceptor
{
    /// <summary>Gets savepoints actually created during the observed operation.</summary>
    internal int Created { get; private set; }

    /// <inheritdoc />
    public override Task CreatedSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Created++;

        return Task.CompletedTask;
    }
}
