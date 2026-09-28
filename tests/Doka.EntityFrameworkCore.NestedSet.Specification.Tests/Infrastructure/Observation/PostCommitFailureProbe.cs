namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Reports failure only after the database has acknowledged a successful commit.</summary>
internal sealed class PostCommitFailureProbe : DbTransactionInterceptor
{
    /// <summary>Gets the exact exception that must survive attempted rollback and transaction cleanup.</summary>
    internal InvalidOperationException Failure { get; } = new("Injected failure after acknowledged database commit.");

    /// <summary>Gets the number of actual commit acknowledgments observed by this context.</summary>
    internal int CommitNotifications { get; private set; }

    /// <inheritdoc />
    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        CommitNotifications++;

        // WHY: A pre-commit failure proves rollback; this later boundary proves why callers must reconcile exceptions.
        throw Failure;
    }
}
