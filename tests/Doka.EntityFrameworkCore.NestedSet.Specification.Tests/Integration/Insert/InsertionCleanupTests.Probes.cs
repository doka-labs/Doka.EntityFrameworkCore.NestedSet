namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class InsertionCleanupTests
{
    /// <summary>Fails after the real INSERT wave, while the hierarchy transaction can still roll back.</summary>
    private sealed class InsertionFailureProbe : SaveChangesInterceptor
    {
        private readonly CancellationTokenSource? _cancellation;

        /// <summary>Creates a stable primary failure so the test can verify its identity survives cleanup.</summary>
        internal InsertionFailureProbe(
            CancellationTokenSource? cancellation
        )
        {
            _cancellation = cancellation;
            Failure = cancellation is null
                ? new InsertionOperationException()
                : new OperationCanceledException("Injected post-save cancellation.", cancellation.Token);
        }

        /// <summary>Gets the exact exception raised after the database saved the input nodes.</summary>
        internal Exception Failure { get; }

        /// <summary>Gets whether the database INSERT wave completed before fault injection.</summary>
        internal bool Saved { get; private set; }

        /// <inheritdoc />
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            Saved = true;

            if (_cancellation is not null)
            {
                await _cancellation.CancelAsync();
            }

            throw Failure;
        }
    }

    /// <summary>Identifies the original operation failure independently of tracker cleanup.</summary>
    private sealed class InsertionOperationException : Exception;

    /// <summary>Identifies an application state callback that fails during owned tracker cleanup.</summary>
    private sealed class InsertionCleanupException : Exception;
}
