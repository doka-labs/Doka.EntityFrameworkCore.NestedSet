namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks command-boundary cancellation evidence without executing a provider command.</summary>
public sealed class CompositionCommandProbeTests
{
    /// <summary>Retains the exact caller token even when cancellation precedes the provider dispatch.</summary>
    /// <param name="canceled">Whether the token was canceled before the command boundary.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderDispatchRecordsTheOriginalCancellationToken(
        bool canceled
    )
    {
        // Arrange
        var probe = new CompositionCommandProbe();
        await using var command = new SqliteCommand();
        using var cancellation = new CancellationTokenSource();

        if (canceled)
        {
            await cancellation.CancelAsync();
        }

        // Act
        await probe.ReaderExecutingAsync(command, null!, default, cancellation.Token);

        // Assert
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(cancellation.Token, probe.ReaderCancellationToken);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>Fixture reset removes the previous dispatch token together with its command count.</summary>
    [Fact]
    public async Task ResetClearsCapturedReaderCancellation()
    {
        // Arrange
        var probe = new CompositionCommandProbe();
        await using var command = new SqliteCommand();
        using var cancellation = new CancellationTokenSource();
        await probe.ReaderExecutingAsync(command, null!, default, cancellation.Token);

        // Act
        probe.Reset();

        // Assert
        Assert.Null(probe.ReaderCancellationToken);
        Assert.Equal(0, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>Nonreader callbacks cannot fabricate evidence that a query reader received a token.</summary>
    /// <param name="scalar">Whether the callback belongs to a scalar instead of a nonquery command.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonReaderDispatchDoesNotInventReaderCancellation(
        bool scalar
    )
    {
        // Arrange
        var probe = new CompositionCommandProbe();
        await using var command = new SqliteCommand();
        using var cancellation = new CancellationTokenSource();

        // Act
        if (scalar)
        {
            await probe.ScalarExecutingAsync(command, null!, default, cancellation.Token);
        }
        else
        {
            await probe.NonQueryExecutingAsync(command, null!, default, cancellation.Token);
        }

        // Assert
        Assert.Null(probe.ReaderCancellationToken);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }
}
