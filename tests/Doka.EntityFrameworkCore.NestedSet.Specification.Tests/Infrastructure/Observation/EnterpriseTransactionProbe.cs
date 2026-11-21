namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Injects cleanup failures without replacing the earlier operation failure.</summary>
public sealed class EnterpriseTransactionProbe : DbTransactionInterceptor
{
    /// <summary>Gets or sets whether the rollback of a service-owned transaction fails.</summary>
    public bool FailOwnedRollback { get; set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (FailOwnedRollback)
        {
            throw new InjectedCleanupException();
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => throw new InjectedCleanupException();
}
