extern alias UserGroupsSample;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>Cancellation after opening closes the owned connection before a native transaction begins.</summary>
    /// <returns>A task completing after the synchronous-begin boundary has been checked.</returns>
    [Fact]
    public async Task CancellationAfterOpenIsObservedBeforeNativeBegin()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        await using var connection = new CancelingSnapshotConnection(stop);
        var observer = new CancelAfterSnapshotOpen(stop);
        await using var context = new SampleLifecycleContext(new DbContextOptionsBuilder<SampleLifecycleContext>()
            .UseSqlite(connection).AddInterceptors(observer).Options);

        var readInvoked = false;

        // Act
        var error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync(context, _ =>
        {
            readInvoked = true;

            return Task.FromResult(1);
        }, stop.Token));

        // Assert
        Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.False(readInvoked);
        Assert.Equal(1, observer.Opened);
        Assert.Null(connection.Begun);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Cancellation during SQLite's sync-only deferred begin disposes native state before any read.</summary>
    /// <param name="alreadyOpen">Whether the connection belongs to the caller before the snapshot starts.</param>
    /// <returns>A task completing after cancellation and native transaction cleanup have been observed.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterNativeBeginRestoresConnectionOwnership(bool alreadyOpen)
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        await using var connection = new CancelingSnapshotConnection(stop);
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
        }, stop.Token));

        var connectionState = connection.State;
        var remainingNativeConnection = connection.Begun?.Connection;

        if (alreadyOpen)
        {
            await context.Database.CloseConnectionAsync();
        }

        // Assert
        Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.False(readInvoked);
        Assert.NotNull(connection.Begun);
        Assert.Null(remainingNativeConnection);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, connectionState);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Injects cancellation immediately after the real synchronous driver begin has completed.</summary>
    private sealed class CancelingSnapshotConnection(CancellationTokenSource stop)
        : SqliteConnection("Data Source=:memory:")
    {
        /// <summary>Gets the native transaction whose disposal must precede returning the cancellation.</summary>
        internal SqliteTransaction? Begun { get; private set; }

        /// <inheritdoc />
        public override SqliteTransaction BeginTransaction(IsolationLevel isolationLevel, bool deferred)
        {
            Begun = base.BeginTransaction(isolationLevel, deferred);

            // WHY: This driver hook is synchronous. Cancel before returning so the post-begin guard is
            // tested deterministically, rather than racing a timer or dispatching unobserved cancellation work.
            // ReSharper disable once MethodHasAsyncOverload
            stop.Cancel();

            return Begun;
        }
    }

    /// <summary>Cancels after the physical open, before control returns to the snapshot helper.</summary>
    private sealed class CancelAfterSnapshotOpen(CancellationTokenSource stop) : DbConnectionInterceptor
    {
        /// <summary>Gets the number of completed physical opens observed by the test.</summary>
        internal int Opened { get; private set; }

        /// <inheritdoc />
        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Opened++;
            await stop.CancelAsync();
        }
    }
}
