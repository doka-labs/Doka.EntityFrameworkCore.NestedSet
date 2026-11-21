using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>A dense streamed grid preserves every ordered value and completes and disposes its reader.</summary>
    [Fact]
    public async Task CreateGridAsync_DenseStreamedGrid_PreservesValuesAndReaderOwnership()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(
            Request() with
            {
                AnchorIds = Enumerable
                    .Range(1, 64)
                    .Select(SignalReportDataset.Id)
                    .ToArray(),
                ChannelIds = Enumerable
                    .Range(101, 32)
                    .Select(SignalReportDataset.Id)
                    .ToArray(),
                Sources = [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst],
            });

        var totals = snapshot
            .AnchorIds
            .SelectMany(anchor => snapshot.ChannelIds.SelectMany(signal =>
                snapshot.Sources.Select(source => Total(
                    anchor,
                    signal,
                    source,
                    source == CapturePort.Pulse
                        ? 0
                        : 13))))
            .ToArray();

        var probe = new ReaderProbe();
        var rows = ReadRows(totals, probe, CancellationToken.None);

        // Act
        var cells = await SignalReportValidation.CreateGridAsync(snapshot, rows, CancellationToken.None);

        // Assert
        Assert.Equal(64 * 32 * 3, cells.Length);
        Assert.All(cells, cell => Assert.True(cell.HasData));
        Assert.Equal(0, cells[1].Value);
        Assert.Equal(13, cells[^1].Value);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(totals.Length, probe.ReadCount);
    }
}
