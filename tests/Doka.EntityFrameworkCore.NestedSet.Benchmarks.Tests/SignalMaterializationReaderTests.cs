namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Preserves cancellation and unconditional disposal in the benchmark's actual scalar readers.</summary>
public sealed class SignalMaterializationReaderTests
{
    /// <summary>Explicit None and live tokens retain complete enumeration and reader ownership.</summary>
    /// <param name="cancellable">Whether the reader receives a live token or explicit None.</param>
    /// <param name="suspend">Whether its first read crosses an asynchronous boundary.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExplicitTokenPreservesCompleteReaderLifecycle(
        bool cancellable,
        bool suspend
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var token = cancellable
            ? cancellation.Token
            : CancellationToken.None;

        var rows = new SignalMaterializationRows<int>([1, 2], suspend);
        var values = new List<int>();

        // Act
        await foreach (var value in rows.WithCancellation(token))
        {
            values.Add(value);
        }

        // Assert
        Assert.Equal([1, 2], values);
        Assert.Equal(1, rows.Enumerations);
        Assert.Equal(1, rows.Disposals);
        Assert.Equal(2, rows.RowsRead);
        Assert.Equal(1, rows.EndOfStreams);
    }

    /// <summary>Cancellation between rows neither advances the reader nor prevents token-free disposal.</summary>
    [Fact]
    public async Task CanceledReaderRejectsNextReadAndStillDisposes()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var rows = new SignalMaterializationRows<int>([1, 2], false);
        var reader = rows.GetAsyncEnumerator(cancellation.Token);
        var firstRead = await reader.MoveNextAsync();
        await cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(async () =>
        {
            await reader.MoveNextAsync();
        });

        await reader.DisposeAsync();

        // Assert
        var canceled = Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(firstRead);
        Assert.Equal(1, reader.Current);
        Assert.Equal(1, rows.Enumerations);
        Assert.Equal(1, rows.Disposals);
        Assert.Equal(1, rows.RowsRead);
        Assert.Equal(0, rows.EndOfStreams);
    }

    /// <summary>A pre-canceled enumeration never acquires a reader or creates cleanup work.</summary>
    [Fact]
    public async Task PreCanceledReaderIsRejectedBeforeAcquisition()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var rows = new SignalMaterializationRows<int>([1], true);

        // Act
        var error = Record.Exception(() => rows.GetAsyncEnumerator(cancellation.Token));

        // Assert
        var canceled = Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, rows.Enumerations);
        Assert.Equal(0, rows.Disposals);
    }
}
