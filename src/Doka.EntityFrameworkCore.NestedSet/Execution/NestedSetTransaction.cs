namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Owns one transaction or caller savepoint shared by structural mutations and coordinated saves.</summary>
internal static class NestedSetTransaction
{
    /// <summary>Runs an operation inside the required provider isolation without accepting tracked changes.</summary>
    /// <param name="context">The caller-owned context whose current EF transaction is discovered.</param>
    /// <param name="operation">The work that acquires its locks before reading or writing structure.</param>
    /// <param name="cancellationToken">The token for forward progress, excluding failure cleanup.</param>
    /// <returns>A task that completes after owned commit/disposal or caller savepoint release.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Ambient, retry, isolation, or savepoint requirements are violated.
    /// </exception>
    /// <exception cref="NotSupportedException">The context uses an unsupported relational provider.</exception>
    /// <exception cref="AggregateException">Operation failure was followed by rollback or disposal failure.</exception>
    internal static async Task ExecuteAsync(
        DbContext context,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (System.Transactions.Transaction.Current is not null)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidTransaction,
                "Ambient transactions are not supported. Use an explicit EF Core transaction.");
        }

        var provider = NestedSetProviderCapabilities.Resolve(context);
        var isolation = provider.RequiredIsolation;
        var callerTransaction = context.Database.CurrentTransaction;
        if (callerTransaction is not null)
        {
            // WHY: EF requires a user-started transaction to live inside one manually invoked execution-strategy
            // delegate. That delegate owns replay and commit; this library owns only its operation savepoint.
            if (ExecutionStrategy.Current?.RetriesOnFailure != true
                && context.Database.CreateExecutionStrategy()
                    .RetriesOnFailure)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidTransaction,
                    "Execute the caller transaction through Database.CreateExecutionStrategy().ExecuteAsync(...).");
            }

            await ExecuteInCallerTransactionAsync(
                    context,
                    callerTransaction,
                    isolation,
                    provider.Kind,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // WHY: Retrying only this library call would exclude surrounding application writes, while replay after an
        // uncertain owned commit could duplicate a completed structural change. The application must define the
        // complete repeatable unit and begin its explicit transaction inside that execution-strategy delegate.
        if (ExecutionStrategy.Current?.RetriesOnFailure == true
            || context.Database.CreateExecutionStrategy()
                .RetriesOnFailure)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidTransaction,
                "A retrying execution strategy requires an explicit caller transaction created inside its delegate.");
        }

        await ExecuteInOwnedTransactionAsync(context, isolation, operation, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Owns commit, failure rollback, and disposal without losing any earlier exception.</summary>
    /// <param name="context">The context whose connection owns this transaction.</param>
    /// <param name="isolation">The provider-specific isolation required by the hierarchy locking protocol.</param>
    /// <param name="operation">The structural reads and writes to execute after acquiring the write lock.</param>
    /// <param name="cancellationToken">The token used for forward progress, excluding rollback and disposal.</param>
    /// <returns>A task that completes after the transaction commits and is disposed.</returns>
    /// <exception cref="AggregateException">Rollback or disposal failed after an earlier failure.</exception>
    private static async Task ExecuteInOwnedTransactionAsync(
        DbContext context,
        IsolationLevel isolation,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        // WHY: SQLite acquires its writer lock at BEGIN IMMEDIATE; later lock-row writes do not measure that wait.
        var transaction = isolation == IsolationLevel.Serializable
            ? await NestedSetTelemetry
                .MeasureLockAsync(token => context.Database.BeginTransactionAsync(isolation, token), cancellationToken)
                .ConfigureAwait(false)
            : await context
                .Database
                .BeginTransactionAsync(isolation, cancellationToken)
                .ConfigureAwait(false);

        Exception? failure = null;

        try
        {
            await operation(cancellationToken)
                .ConfigureAwait(false);

            await transaction
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationError)
        {
            failure = operationError;

            try
            {
                // WHY: Cancellation can interrupt a partly applied edit; cleanup must still attempt full rollback.
                await transaction
                    .RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception rollbackError)
            {
                failure = new AggregateException(
                    "Nested-set transaction rollback failed. Discard the context.",
                    operationError,
                    rollbackError);
            }
        }

        try
        {
            // WHY: Explicit disposal preserves operation and rollback errors if releasing the transaction also fails.
            await transaction
                .DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception disposalError)
        {
            failure = failure is null
                ? disposalError
                : new AggregateException(
                    "Nested-set transaction disposal failed. Discard the context.",
                    failure,
                    disposalError);
        }

        if (failure is not null)
        {
            // WHY: Deferring the throw until disposal finishes must not replace the original exception's stack trace.
            System
                .Runtime
                .ExceptionServices
                .ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }
    }

    /// <summary>Protects one operation while preserving the caller's prior writes and transaction ownership.</summary>
    /// <param name="context">The context used to read SQL Server session options.</param>
    /// <param name="transaction">The existing EF transaction, which this method never commits or disposes.</param>
    /// <param name="isolation">The isolation required for lock-protected structural reads on the provider.</param>
    /// <param name="providerKind">The verified provider dialect used for transaction preconditions.</param>
    /// <param name="operation">The structural operation to run after acquiring the configured write lock.</param>
    /// <param name="cancellationToken">The token used for savepoint creation, locking, operation, and release.</param>
    /// <returns>A task that completes when the operation and savepoint release succeed.</returns>
    /// <exception cref="InvalidOperationException">The isolation or savepoint support is unsuitable.</exception>
    /// <exception cref="AggregateException">Operation failure was followed by savepoint cleanup failure.</exception>
    private static async Task ExecuteInCallerTransactionAsync(
        DbContext context,
        IDbContextTransaction transaction,
        IsolationLevel isolation,
        NestedSetProviderKind providerKind,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        if (!transaction.SupportsSavepoints)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidTransaction,
                "The caller transaction must support savepoints.");
        }

        if (transaction.GetDbTransaction().IsolationLevel != isolation)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidTransaction,
                $"The caller transaction must use {isolation} isolation.");
        }

        if (providerKind == NestedSetProviderKind.SqlServer)
        {
            // WHY: Under XACT_ABORT ON a caught duplicate-key error dooms the whole SQL Server transaction.
            // A savepoint cannot restore it, so reject that session mode before acquiring any tree lock.
            // SQL Server documents 16384 as the XACT_ABORT bit in @@OPTIONS.
            var options = await context
                .Database
                .SqlQueryRaw<int>("SELECT @@OPTIONS & 16384 AS [Value]")
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            if (options != 0)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidTransaction,
                    "SQL Server caller transactions must use SET XACT_ABORT OFF for nested-set savepoints.");
            }
        }

        // WHY: A distinct identifier avoids application/EF collisions and respects SQL Server's 32-character limit.
        var savepoint = $"NS{Guid.NewGuid():N}"[..32];

        await transaction
            .CreateSavepointAsync(savepoint, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await operation(cancellationToken)
                .ConfigureAwait(false);

            await transaction
                .ReleaseSavepointAsync(savepoint, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationError)
        {
            await RollbackSavepointAsync(transaction, savepoint, operationError)
                .ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>Restores the caller's pre-operation state without allowing cancellation to bypass cleanup.</summary>
    /// <param name="transaction">The caller-owned transaction whose savepoint protects this operation.</param>
    /// <param name="savepoint">The unique savepoint created before acquiring the hierarchy lock.</param>
    /// <param name="operationError">The original failure, preserved if rollback or release also fails.</param>
    /// <returns>A task that completes only after rollback and savepoint release both succeed.</returns>
    /// <exception cref="AggregateException">Cleanup failed; discard the caller transaction and context.</exception>
    private static async Task RollbackSavepointAsync(
        IDbContextTransaction transaction,
        string savepoint,
        Exception operationError
    )
    {
        try
        {
            // WHY: A canceled request must not prevent undoing the partially applied structural operation.
            await transaction
                .RollbackToSavepointAsync(savepoint, CancellationToken.None)
                .ConfigureAwait(false);

            await transaction
                .ReleaseSavepointAsync(savepoint, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception cleanupError)
        {
            // WHY: Neither the operation failure nor an uncertain caller transaction state may be hidden.
            throw new AggregateException(
                "Nested-set savepoint cleanup failed. Discard the caller transaction and context.",
                operationError,
                cleanupError);
        }
    }
}
