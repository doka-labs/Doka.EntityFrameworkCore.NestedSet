extern alias UserGroupsSample;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>A callback failure after a physical open closes only a connection opened by this attempt.</summary>
    /// <param name="cancel">Whether the after-open callback cancels instead of throwing its read exception.</param>
    /// <param name="alreadyOpen">Whether a caller-owned open bypasses the armed physical-open callback.</param>
    /// <returns>A task completing after physical connection cleanup and caller-owned controls are checked.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedAfterOpenNotificationRestoresOnlyOwnedPhysicalConnection(bool cancel, bool alreadyOpen)
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        var observer = new FailingSnapshotOpenNotification(stop, cancel);
        await using var context = new SampleLifecycleContext(new DbContextOptionsBuilder<SampleLifecycleContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(observer).Options);

        if (alreadyOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        observer.Armed = true;
        var readInvoked = false;

        // Act
        var error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync(context, _ =>
        {
            readInvoked = true;

            return Task.FromResult(1);
        }, stop.Token));

        var state = context.Database.GetDbConnection().State;

        if (alreadyOpen)
        {
            await context.Database.CloseConnectionAsync();
        }

        // Assert
        if (alreadyOpen)
        {
            Assert.Null(error);
        }
        else if (cancel)
        {
            Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        }
        else
        {
            Assert.Same(observer.Failure, error);
        }

        Assert.Equal(alreadyOpen, readInvoked);
        Assert.Equal(alreadyOpen ? 0 : 1, observer.FailedOpens);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, state);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Fails only after the real physical open, before EF finishes recording that open.</summary>
    private sealed class FailingSnapshotOpenNotification(CancellationTokenSource stop, bool cancel)
        : DbConnectionInterceptor
    {
        /// <summary>Gets or sets whether setup has completed and the next physical open must fail.</summary>
        internal bool Armed { get; set; }

        /// <summary>Gets the number of deliberately failed physical-open notifications.</summary>
        internal int FailedOpens { get; private set; }

        /// <summary>Gets the exact exception expected to survive physical connection cleanup unchanged.</summary>
        internal SnapshotReadException Failure { get; } = new();

        /// <inheritdoc />
        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (!Armed)
            {
                return;
            }

            FailedOpens++;

            if (cancel)
            {
                await stop.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw Failure;
        }
    }
}
