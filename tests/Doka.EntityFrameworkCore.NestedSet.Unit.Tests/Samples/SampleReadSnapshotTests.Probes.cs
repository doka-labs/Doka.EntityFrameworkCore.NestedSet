using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>Creates a closed SQLite context whose first connection attempt fails explicitly.</summary>
    private static SampleLifecycleContext ClosedContext(
        BeforeSnapshotReadBoundary boundary
    )
    {
        var options = new DbContextOptionsBuilder<SampleLifecycleContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(boundary)
            .Options;

        return new SampleLifecycleContext(options);
    }

    /// <summary>Adds transaction observations to the existing isolated lifecycle fixture.</summary>
    private static SampleLifecycleContext ObservedContext(
        SampleLifecycleDirectory directory,
        SnapshotTransactionObserver observer
    )
    {
        var configuration = directory.Configuration();
        var configured = configuration.CreateOptions<SampleLifecycleContext>();
        var options = new DbContextOptionsBuilder<SampleLifecycleContext>(configured)
            .AddInterceptors(observer)
            .Options;

        var context = new SampleLifecycleContext(options);
        var connection = new SqliteConnectionStringBuilder(context.Database.GetDbConnection().ConnectionString)
        {
            Pooling = false,
        };

        // WHY: The test owns a temporary file; closing this context must release it without resetting sibling pools.
        context.Database.SetConnectionString(connection.ConnectionString);

        return context;
    }

    /// <summary>Counts completed asynchronous transaction events, excluding setup after Reset.</summary>
    private sealed class SnapshotTransactionObserver : DbTransactionInterceptor
    {
        /// <summary>Gets the number of transactions started after setup.</summary>
        internal int Started { get; private set; }

        /// <summary>Gets the number of caller-created native transactions attached to EF.</summary>
        internal int Attached { get; private set; }

        /// <summary>Gets the number of successfully committed transactions after setup.</summary>
        internal int Committed { get; private set; }

        /// <summary>Begins observing only the operation being tested.</summary>
        internal void Reset()
        {
            Started = 0;
            Attached = 0;
            Committed = 0;
        }

        /// <inheritdoc />
        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default
        )
        {
            Started++;

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override ValueTask<DbTransaction> TransactionUsedAsync(
            DbConnection connection,
            TransactionEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default
        )
        {
            Attached++;

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Committed++;

            return Task.CompletedTask;
        }
    }

    /// <summary>Stops synchronous and asynchronous connection attempts at the snapshot boundary.</summary>
    private sealed class BeforeSnapshotReadBoundary : DbConnectionInterceptor
    {
        /// <summary>Gets the number of connection starts attempted by the operation.</summary>
        internal int ConnectionStarts { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            ConnectionStarts++;

            throw new InvalidOperationException("The snapshot guard permitted database I/O.");
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            ConnectionStarts++;

            throw new InvalidOperationException("The snapshot guard permitted asynchronous database I/O.");
        }
    }

    /// <summary>Distinguishes the deliberately failing read delegate from infrastructure failures.</summary>
    private sealed class SnapshotReadException : Exception;
}
