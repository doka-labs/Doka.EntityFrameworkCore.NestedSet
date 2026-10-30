extern alias UserGroupsSample;

using Microsoft.EntityFrameworkCore.Storage;
using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>An owned snapshot commits at Serializable isolation without saving tracked writes.</summary>
    /// <param name="alreadyOpen">Whether the caller owns a pre-existing connection-open reference.</param>
    /// <returns>A task completing after the real read transaction has been observed.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSnapshotCommitsAndDisposesWithoutSavingTrackedChanges(bool alreadyOpen)
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var observer = new SnapshotTransactionObserver();
        await using var context = ObservedContext(directory, observer);
        await SampleLifecycleDirectory.SeedAsync(context, "persisted", CancellationToken.None);
        var pending = context.Sentinels.Local.Single();
        pending.Value = "unsaved";
        context.ChangeTracker.DetectChanges();

        if (alreadyOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        observer.Reset();
        using var stop = new CancellationTokenSource();
        var readToken = CancellationToken.None;
        IsolationLevel? actualIsolation = null;
        var reads = 0;

        // Act
        var result = await SampleReadSnapshot.RunAsync(context, async token =>
        {
            reads++;
            readToken = token;
            actualIsolation = context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel;

            return await context.Sentinels.AsNoTracking().Select(row => row.Value).SingleAsync(token);
        }, stop.Token);

        var connectionState = context.Database.GetDbConnection().State;

        if (alreadyOpen)
        {
            await context.Database.CloseConnectionAsync();
        }

        // Assert
        Assert.Equal("persisted", result);
        Assert.Equal(IsolationLevel.Serializable, actualIsolation);
        Assert.Equal(stop.Token, readToken);
        Assert.Equal(1, reads);
        Assert.Equal(0, observer.Started);
        Assert.Equal(1, observer.Attached);
        Assert.Equal(1, observer.Committed);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, connectionState);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal(EntityState.Modified, context.Entry(pending).State);
        Assert.Equal("unsaved", pending.Value);
    }

    /// <summary>An owned snapshot is disposed without committing when its buffered read fails or is canceled.</summary>
    /// <param name="cancel">Whether the read cancels its token instead of throwing a read failure.</param>
    /// <param name="alreadyOpen">Whether the caller owns a pre-existing connection-open reference.</param>
    /// <returns>A task completing after the failed read and transaction cleanup are observed.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedOwnedSnapshotIsDisposedWithoutCommit(
        bool cancel,
        bool alreadyOpen
    )
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var observer = new SnapshotTransactionObserver();
        await using var context = ObservedContext(directory, observer);
        await SampleLifecycleDirectory.SeedAsync(context, "persisted", CancellationToken.None);

        if (alreadyOpen)
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
        }

        observer.Reset();
        using var stop = new CancellationTokenSource();
        var failure = new SnapshotReadException();
        var reads = 0;
        SqliteTransaction? native = null;

        // Act
        var error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync<int>(context, async token =>
        {
            native = (SqliteTransaction)context.Database.CurrentTransaction!.GetDbTransaction();
            _ = await context.Sentinels.AsNoTracking().Select(row => row.Id).SingleAsync(token);
            reads++;

            if (cancel)
            {
                await stop.CancelAsync();
                token.ThrowIfCancellationRequested();
            }

            throw failure;
        }, stop.Token));

        var connectionState = context.Database.GetDbConnection().State;
        var remainingNativeConnection = native?.Connection;

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
            Assert.Same(failure, error);
        }

        Assert.Equal(1, reads);
        Assert.Equal(0, observer.Started);
        Assert.Equal(1, observer.Attached);
        Assert.Equal(0, observer.Committed);
        Assert.NotNull(native);
        Assert.Null(remainingNativeConnection);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(alreadyOpen ? ConnectionState.Open : ConnectionState.Closed, connectionState);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Every read outcome preserves the caller's live transaction and preceding application writes.</summary>
    /// <param name="completion">The deliberate report outcome inside an already active caller transaction.</param>
    /// <returns>A task completing after transaction ownership has been checked.</returns>
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task CallerTransactionRemainsOwnedByCaller(
        string completion
    )
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        var observer = new SnapshotTransactionObserver();
        await using var context = ObservedContext(directory, observer);
        await SampleLifecycleDirectory.SeedAsync(context, "persisted", CancellationToken.None);
        await using var caller = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, CancellationToken.None);

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE SampleSentinels SET Value = 'caller-change' WHERE Id = 1", CancellationToken.None);

        observer.Reset();
        using var stop = new CancellationTokenSource();
        var failure = new SnapshotReadException();
        var reads = 0;
        string? result = null;
        string? valueRead = null;
        var readToken = CancellationToken.None;

        // Act
        var error = await Record.ExceptionAsync(async () =>
        {
            result = await SampleReadSnapshot.RunAsync(context, async token =>
            {
                readToken = token;
                valueRead = await context.Sentinels.AsNoTracking().Select(row => row.Value).SingleAsync(token);
                reads++;

                if (completion == "failure")
                {
                    throw failure;
                }

                if (completion == "cancellation")
                {
                    await stop.CancelAsync();
                    token.ThrowIfCancellationRequested();
                }

                return valueRead;
            }, stop.Token);
        });

        // Assert
        switch (completion)
        {
            case "success":
                Assert.Null(error);
                Assert.Equal("caller-change", result);
                break;
            case "failure":
                Assert.Same(failure, error);
                break;
            case "cancellation":
                Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
                break;
        }

        Assert.Equal(1, reads);
        Assert.Equal("caller-change", valueRead);
        Assert.Equal(stop.Token, readToken);
        Assert.Equal(0, observer.Started);
        Assert.Equal(0, observer.Attached);
        Assert.Equal(0, observer.Committed);
        Assert.Same(caller, context.Database.CurrentTransaction);
        Assert.Same(context.Database.GetDbConnection(), caller.GetDbTransaction().Connection);
        Assert.Equal(IsolationLevel.Serializable, caller.GetDbTransaction().IsolationLevel);
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
    }
}
