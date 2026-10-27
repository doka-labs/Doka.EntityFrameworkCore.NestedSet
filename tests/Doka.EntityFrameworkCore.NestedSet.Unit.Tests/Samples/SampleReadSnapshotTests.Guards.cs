extern alias UserGroupsSample;

using AmbientFlow = System.Transactions.TransactionScopeAsyncFlowOption;
using AmbientScope = System.Transactions.TransactionScope;
using SampleReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SampleReadSnapshotTests
{
    /// <summary>A failed owned connection open neither starts a read nor acquires a transaction reference.</summary>
    /// <returns>A task completing after the actual snapshot helper's open failure has been checked.</returns>
    [Fact]
    public async Task OwnedConnectionOpenFailureDoesNotStartTheRead()
    {
        // Arrange
        var boundary = new BeforeSnapshotReadBoundary();
        await using var context = ClosedContext(boundary);
        var readInvoked = false;

        // Act
        var error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync(context, _ =>
        {
            readInvoked = true;

            return Task.FromResult(1);
        }, CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.False(readInvoked);
        Assert.Equal(1, boundary.ConnectionStarts);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>A missing context or read delegate is rejected before the operation can begin.</summary>
    /// <param name="parameter">The required argument deliberately omitted.</param>
    /// <returns>A task that completes after the argument failure is observed.</returns>
    [Theory]
    [InlineData("context")]
    [InlineData("read")]
    public async Task RequiredArgumentsAreRejected(
        string parameter
    )
    {
        // Arrange
        var boundary = new BeforeSnapshotReadBoundary();
        await using var context = ClosedContext(boundary);

        // Act
        var error = await Record.ExceptionAsync(() => parameter == "context"
            ? SampleReadSnapshot.RunAsync<int>(null!, _ => Task.FromResult(1), CancellationToken.None)
            : SampleReadSnapshot.RunAsync<int>(context, null!, CancellationToken.None));

        // Assert
        Assert.Equal(parameter, Assert.IsType<ArgumentNullException>(error).ParamName);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>An already canceled request neither opens a connection nor invokes the report delegate.</summary>
    /// <returns>A task that completes after cancellation is observed.</returns>
    [Fact]
    public async Task CancellationIsObservedBeforeIo()
    {
        // Arrange
        var boundary = new BeforeSnapshotReadBoundary();
        await using var context = ClosedContext(boundary);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
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
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>An ambient transaction is rejected before any EF read or database connection attempt.</summary>
    /// <returns>A task that completes after the unsupported transaction boundary is observed.</returns>
    [Fact]
    public async Task AmbientTransactionsAreRejectedBeforeIo()
    {
        // Arrange
        var boundary = new BeforeSnapshotReadBoundary();
        await using var context = ClosedContext(boundary);
        using var ambient = new AmbientScope(AmbientFlow.Enabled);
        var readInvoked = false;

        // Act
        var error = await Record.ExceptionAsync(() => SampleReadSnapshot.RunAsync(context, _ =>
        {
            readInvoked = true;

            return Task.FromResult(1);
        }, CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.False(readInvoked);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>The early-boundary observer rejects an actual connection attempt.</summary>
    /// <returns>A task completing after the deliberate connection failure is observed.</returns>
    [Fact]
    public async Task BeforeReadBoundaryRejectsActualConnectionAttempts()
    {
        // Arrange
        var boundary = new BeforeSnapshotReadBoundary();
        await using var context = ClosedContext(boundary);

        // Act
        var error = await Record.ExceptionAsync(() => context.Database.OpenConnectionAsync(CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(1, boundary.ConnectionStarts);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
