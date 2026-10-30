extern alias UserGroupsSample;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using AmbientTransaction = System.Transactions.Transaction;
using CommittableTransaction = System.Transactions.CommittableTransaction;
using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>An EF adoption callback failure releases its extra open reference and the native transaction.</summary>
    /// <param name="cancel">Whether adoption cancels instead of throwing a deliberate exception.</param>
    /// <param name="alreadyOpen">Whether the caller already owns an open connection.</param>
    /// <returns>A task completing after both transaction lifetimes and connection ownership are checked.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedAdoptionReleasesItsConnectionReference(bool cancel, bool alreadyOpen)
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        var observer = new FailingSnapshotAdoption(stop, cancel);
        await using var context = new SampleLifecycleContext(new DbContextOptionsBuilder<SampleLifecycleContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(observer).Options);

        if (alreadyOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        var readInvoked = false;

        // Act
        var error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync(context, _ =>
        {
            readInvoked = true;

            return Task.FromResult(1);
        }, stop.Token));

        var state = context.Database.GetDbConnection().State;
        var remainingNativeConnection = observer.Native?.Connection;

        if (alreadyOpen)
        {
            await context.Database.CloseConnectionAsync();
        }

        // Assert
        if (cancel)
        {
            Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        }
        else
        {
            Assert.Same(observer.Failure, error);
        }

        Assert.False(readInvoked);
        Assert.NotNull(observer.Native);
        Assert.Null(remainingNativeConnection);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, state);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>A failure before native begin never acquires an EF adoption reference.</summary>
    /// <param name="alreadyOpen">Whether the caller already owns an open connection.</param>
    /// <returns>A task completing after the helper's sole open reference has been balanced.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedNativeBeginPreservesCallerConnectionOwnership(bool alreadyOpen)
    {
        // Arrange
        await using var connection = new FailingSnapshotBeginConnection();
        await using var context = new SampleLifecycleContext(new DbContextOptionsBuilder<SampleLifecycleContext>()
            .UseSqlite(connection).Options);

        if (alreadyOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        var readInvoked = false;

        // Act
        var error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync(context, _ =>
        {
            readInvoked = true;

            return Task.FromResult(1);
        }, CancellationToken.None));

        var state = connection.State;

        if (alreadyOpen)
        {
            await context.Database.CloseConnectionAsync();
        }

        // Assert
        Assert.Same(connection.Failure, error);
        Assert.False(readInvoked);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, state);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>A newly introduced ambient transaction is rejected before EF acquires an adoption reference.</summary>
    /// <param name="alreadyOpen">Whether the caller already owns an open connection.</param>
    /// <returns>A task completing after the before-adoption failure preserves the original open ownership.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeforeAdoptionFailureDoesNotCloseAnUnacquiredReference(bool alreadyOpen)
    {
        // Arrange
        using var ambient = new CommittableTransaction();
        var previousAmbient = AmbientTransaction.Current;
        await using var connection = new AmbientSnapshotBeginConnection(ambient);
        await using var context = new SampleLifecycleContext(new DbContextOptionsBuilder<SampleLifecycleContext>()
            .UseSqlite(connection).Options);

        if (alreadyOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        var readInvoked = false;
        Exception? error;
        ConnectionState state;
        DbConnection? remainingNativeConnection;

        // Act
        try
        {
            error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync(context, _ =>
            {
                readInvoked = true;

                return Task.FromResult(1);
            }, CancellationToken.None));

            state = connection.State;
            remainingNativeConnection = connection.Begun?.Connection;
        }
        finally
        {
            AmbientTransaction.Current = previousAmbient;
        }

        if (alreadyOpen)
        {
            await context.Database.CloseConnectionAsync();
        }

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.False(readInvoked);
        Assert.NotNull(connection.Begun);
        Assert.Null(remainingNativeConnection);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, state);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Injects failure only after EF has opened its adoption reference.</summary>
    private sealed class FailingSnapshotAdoption(CancellationTokenSource stop, bool cancel) : DbTransactionInterceptor
    {
        /// <summary>Gets the native transaction received by the failed adoption notification.</summary>
        internal DbTransaction? Native { get; private set; }

        /// <summary>Gets the exact exception expected to survive cleanup unchanged.</summary>
        internal SnapshotReadException Failure { get; } = new();

        /// <inheritdoc />
        public override async ValueTask<DbTransaction> TransactionUsedAsync(
            DbConnection connection,
            TransactionEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default
        )
        {
            Native = result;

            if (cancel)
            {
                await stop.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw Failure;
        }
    }

    /// <summary>Fails at the native begin boundary before an EF transaction wrapper exists.</summary>
    private sealed class FailingSnapshotBeginConnection : SqliteConnection
    {
        /// <summary>Creates an isolated in-memory driver connection.</summary>
        public FailingSnapshotBeginConnection() : base("Data Source=:memory:") { }

        /// <summary>Gets the exact exception expected to survive cleanup unchanged.</summary>
        internal SnapshotReadException Failure { get; } = new();

        /// <inheritdoc />
        public override SqliteTransaction BeginTransaction(IsolationLevel isolationLevel, bool deferred)
            => throw Failure;
    }

    /// <summary>Introduces an ambient boundary after the helper's initial guard but before EF adoption.</summary>
    private sealed class AmbientSnapshotBeginConnection(CommittableTransaction ambient)
        : SqliteConnection("Data Source=:memory:")
    {
        /// <summary>Gets the native transaction that must be disposed when adoption is rejected.</summary>
        internal SqliteTransaction? Begun { get; private set; }

        /// <inheritdoc />
        public override SqliteTransaction BeginTransaction(IsolationLevel isolationLevel, bool deferred)
        {
            Begun = base.BeginTransaction(isolationLevel, deferred);
            AmbientTransaction.Current = ambient;

            return Begun;
        }
    }
}
