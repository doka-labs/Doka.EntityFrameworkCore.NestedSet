using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>A dense streamed grid retains only bounded indexes and its final ordered cells.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public async Task DenseStreamedGridRetainsBoundedTemporaryState()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request() with
        {
            AnchorIds = Enumerable.Range(1, 64).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(101, 32).Select(SignalReportDataset.Id).ToArray(),
            Sources = [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst],
        });

        var totals = snapshot.AnchorIds.SelectMany(anchor => snapshot.ChannelIds.SelectMany(signal =>
            snapshot.Sources.Select(source => Total(anchor, signal, source, source == CapturePort.Pulse ? 0 : 13))))
            .ToArray();

        // WHY: This fixed grain measured 858248 B on .NET 10. About 21 KiB of headroom permits
        // runtime variation while detecting excess temporary state during dense asynchronous materialization.
        const long maximumBytes = 880_000;
        for (var warmup = 0; warmup < 8; warmup++)
        {
            _ = await SignalReportValidation.CreateGridAsync(snapshot,
                ReadRows(totals, new ReaderProbe(), CancellationToken.None), CancellationToken.None);
        }

        var probe = new ReaderProbe();
        var rows = ReadRows(totals, probe, CancellationToken.None);
        var thread = Environment.CurrentManagedThreadId;

        // Act
        // WHY: Completed source reads and disposal keep this operation on one thread. Current-thread bytes
        // exclude allocations from concurrently running suites; the assertion below checks that premise.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var cells = await SignalReportValidation.CreateGridAsync(snapshot, rows, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        output.WriteLine($"Dense streamed grid: cells={cells.Length}; allocated={allocated} B; "
            + $"maximum={maximumBytes} B.");
        Assert.Equal(64 * 32 * 3, cells.Length);
        Assert.All(cells, cell => Assert.True(cell.HasData));
        Assert.Equal(0, cells[1].Value);
        Assert.Equal(13, cells[^1].Value);
        Assert.Equal(thread, Environment.CurrentManagedThreadId);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(totals.Length, probe.ReadCount);
        Assert.InRange(allocated, 0, maximumBytes);
    }
}
