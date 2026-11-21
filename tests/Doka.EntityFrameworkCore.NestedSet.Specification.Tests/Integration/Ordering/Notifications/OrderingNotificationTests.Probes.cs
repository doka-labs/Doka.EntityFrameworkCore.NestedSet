namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingNotificationTests
{
    /// <summary>Fails after payload writes, hierarchy repair, and tracked refresh but before database commit.</summary>
    private sealed class CommitFailureProbe : DbTransactionInterceptor
    {
        /// <summary>Gets or sets whether the next commit attempt fails.</summary>
        internal bool FailOnCommit { get; set; } = true;

        /// <summary>Gets the number of save boundaries that reached the actual commit attempt.</summary>
        internal int CommitAttempts { get; private set; }

        /// <summary>Gets the refreshed coordinate visible immediately before the commit attempt.</summary>
        internal long RenamedLeftAtCommit { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            var context = eventData.Context
                ?? throw new InvalidOperationException("The observed transaction has no owning context.");

            CommitAttempts++;
            RenamedLeftAtCommit = context
                .ChangeTracker
                .Entries<OrderingNotificationNode>()
                .Single(entry => entry.Entity.Id == 2)
                .Entity
                .Left;

            if (FailOnCommit)
            {
                // WHY: Throwing here exercises rollback after both database and tracked coordinates were refreshed.
                throw new OrderingInjectedException();
            }

            return ValueTask.FromResult(result);
        }
    }
}
