using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using AmbientTransaction = System.Transactions.Transaction;

namespace Doka.EntityFrameworkCore.NestedSet.Samples;

/// <summary>Gives a complete sample report or supervisor chain one consistent relational read view.</summary>
internal static class SampleReadSnapshot
{
    /// <summary>Uses a qualified caller transaction or owns one complete asynchronous read snapshot.</summary>
    /// <typeparam name="T">The fully buffered report or chain result.</typeparam>
    /// <param name="context">The single context used by every participating read.</param>
    /// <param name="read">The complete operation; every replay creates its own result and frontier state.</param>
    /// <param name="cancellationToken">The token forwarded to the strategy, transaction, and reads.</param>
    /// <returns>The complete result from one successful snapshot attempt.</returns>
    /// <exception cref="InvalidOperationException">
    ///     An ambient, inadequate, or improperly retried transaction is present.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     The context does not use a qualified provider or its SQLite connection lacks the required enlistment contract.
    /// </exception>
    /// <remarks>
    ///     SQL Server requires ALLOW_SNAPSHOT_ISOLATION configured by the database owner in advance.
    ///     This helper never changes database options, saves tracked entities, or commits a caller transaction.
    /// </remarks>
    internal static async Task<T> RunAsync<T>(
        DbContext context,
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(read);
        cancellationToken.ThrowIfCancellationRequested();

        if (AmbientTransaction.Current is not null)
        {
            throw new InvalidOperationException(
                "Use an explicit EF transaction for the complete sample read snapshot.");
        }

        var provider = context.Database.ProviderName;
        var isolation = RequiredIsolation(provider);
        var strategy = context.Database.CreateExecutionStrategy();

        if (context.Database.CurrentTransaction is { } caller)
        {
            RequireIsolation(
                provider,
                caller.GetDbTransaction()
                    .IsolationLevel);

            if (ExecutionStrategy.Current is null
                && strategy.RetriesOnFailure)
            {
                throw new InvalidOperationException(
                    "Run the complete caller transaction inside Database.CreateExecutionStrategy().ExecuteAsync(...).");
            }

            return await read(cancellationToken);
        }

        // WHY: A transient retry must replay the catalog and all report/chain reads together in a new
        // snapshot. Reusing an earlier frontier or retrying a single hop would mix different database views.
        return await strategy.ExecuteAsync(
            async token =>
            {
                if (provider == "Microsoft.EntityFrameworkCore.Sqlite")
                {
                    return await ReadSqliteAsync(context, read, token);
                }

                await using var transaction = await context.Database.BeginTransactionAsync(isolation, token);
                var result = await read(token);
                await transaction.CommitAsync(token);

                return result;
            },
            cancellationToken);
    }

    /// <summary>Owns a deferred SQLite read transaction without reserving the connection's writer lock.</summary>
    /// <typeparam name="T">The buffered result of the complete read attempt.</typeparam>
    /// <param name="context">The context whose existing connection-open ownership must be retained.</param>
    /// <param name="read">All reads belonging to one consistent snapshot attempt.</param>
    /// <param name="cancellationToken">The token observed before and after the sync-only driver begin.</param>
    /// <returns>The buffered result after committing this owned transaction.</returns>
    private static async Task<T> ReadSqliteAsync<T>(
        DbContext context,
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken
    )
    {
        var relational = context.GetService<IRelationalConnection>();
        if (relational is not ITransactionEnlistmentManager enlistment)
        {
            // WHY: IRelationalConnection does not require enlistment support. Reject an unsupported
            // replacement before acquiring an open reference or native transaction that would need cleanup.
            throw new NotSupportedException(
                "The SQLite read snapshot requires a relational connection with the EF enlistment contract.");
        }

        var connection = (SqliteConnection)relational.DbConnection;
        var ownsPhysicalOpen = connection.State == ConnectionState.Closed;

        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }
        catch
        {
            if (ownsPhysicalOpen)
            {
                // WHY: ConnectionOpenedAsync can fail after the physical open but before EF records it.
                // Balance any completed EF open, then close only the handle this attempt acquired if needed.
                await context.Database.CloseConnectionAsync();

                if (connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }

            throw;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: SQLite's inherited async Serializable begin uses BEGIN IMMEDIATE and reserves the writer
            // lock even for a read-only report. Only this synchronous public overload supports deferred BEGIN.
            // Cancellation brackets that unavoidable call; every other ownership operation stays asynchronous.
            // ReSharper disable once MethodHasAsyncOverload
            await using var native = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: EF checks these conditions before acquiring its adoption-open reference. Reject them
            // outside the catch below, which must never release an unacquired caller-owned reference.
            // Read the connection's public enlistment contract: CurrentAmbientTransaction is virtual,
            // so Transaction.Current alone would miss a customized connection's adoption precondition.
            if (enlistment.CurrentAmbientTransaction is not null
                || enlistment.EnlistedTransaction is not null)
            {
                throw new InvalidOperationException(
                    "The SQLite read snapshot cannot attach inside an ambient or enlisted transaction.");
            }

            IDbContextTransaction transaction;

            try
            {
                transaction = await context.Database.UseTransactionAsync(native, cancellationToken)
                    ?? throw new InvalidOperationException("The SQLite read snapshot could not be attached to EF.");
            }
            catch
            {
                // WHY: The SQLite connection is already open and preconditions passed. EF acquires one
                // additional open reference before TransactionUsedAsync; failed adoption has no wrapper
                // to release it. The outer finally separately balances this helper's original open.
                await context.Database.CloseConnectionAsync();

                throw;
            }

            // WHY: UseTransactionAsync does not own its native transaction. Dispose both lifetimes explicitly,
            // with the EF wrapper first, so failures/cancellation roll back native state before returning it.
            await using (transaction)
            {
                var result = await read(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return result;
            }
        }
        finally
        {
            // WHY: Balance only this helper's EF open reference. A connection already opened by the caller
            // stays open; cleanup must still run when the read token has been canceled.
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Selects multi-statement read isolation without querying or changing server settings.</summary>
    /// <param name="provider">The configured EF provider name.</param>
    /// <returns>The isolation used for a transaction owned by the sample.</returns>
    /// <exception cref="NotSupportedException">The provider has not been qualified.</exception>
    internal static IsolationLevel RequiredIsolation(
        string? provider
    ) => provider switch
    {
        "Doka.EntityFrameworkCore.MySql" or "Npgsql.EntityFrameworkCore.PostgreSQL" => IsolationLevel.RepeatableRead,
        "Microsoft.EntityFrameworkCore.SqlServer" => IsolationLevel.Snapshot,
        "Microsoft.EntityFrameworkCore.Sqlite" => IsolationLevel.Serializable,
        _ => throw new NotSupportedException($"Sample read snapshots are not qualified for provider '{provider}'."),
    };

    /// <summary>Rejects weaker caller isolation before the first catalog, report, or supervisor read.</summary>
    /// <param name="provider">The configured EF provider name.</param>
    /// <param name="actual">The actual isolation reported by the caller's driver transaction.</param>
    /// <exception cref="InvalidOperationException">The transaction cannot provide the shared read view.</exception>
    /// <exception cref="NotSupportedException">The provider has not been qualified.</exception>
    internal static void RequireIsolation(
        string? provider,
        IsolationLevel actual
    )
    {
        var required = RequiredIsolation(provider);
        var adequate = actual == IsolationLevel.Serializable
            || actual == required
            || (actual == IsolationLevel.Snapshot
                && provider is "Doka.EntityFrameworkCore.MySql" or "Npgsql.EntityFrameworkCore.PostgreSQL");

        if (!adequate)
        {
            // WHY: SQL Server RCSI stabilizes one statement, and RepeatableRead still permits phantoms.
            // Neither can preserve catalog, ranking, membership, and multiple-hop decisions together.
            throw new InvalidOperationException(
                $"The sample read requires {required} or Serializable isolation; the caller uses {actual}.");
        }
    }
}
