namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ManagedSaveCallbackTests
{
    /// <summary>Runs an application inspection at the asynchronous SavedChanges interceptor boundary.</summary>
    private sealed class PostSaveInspector : SaveChangesInterceptor
    {
        internal Action<DbContext>? OnSaved { get; set; }

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            OnSaved?.Invoke(eventData.Context!);

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Fails once after EF persisted and accepted a save, while the transaction can still roll back.</summary>
    private sealed class PostSaveFailure : SaveChangesInterceptor
    {
        private int _savedCount;

        /// <summary>Gets the one-based save invocation that raises the injected failure.</summary>
        internal int FailOnSave { get; init; } = 1;

        /// <summary>Gets the exact failure raised after the first successful persistence step.</summary>
        internal Exception Error { get; } = new InvalidOperationException("Injected post-save failure.");

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (++_savedCount != FailOnSave)
            {
                return ValueTask.FromResult(result);
            }

            throw Error;
        }
    }

    /// <summary>Replaces the save with a result so EF never reaches its persistence step.</summary>
    private sealed class SuppressingSave : SaveChangesInterceptor
    {
        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }
}
