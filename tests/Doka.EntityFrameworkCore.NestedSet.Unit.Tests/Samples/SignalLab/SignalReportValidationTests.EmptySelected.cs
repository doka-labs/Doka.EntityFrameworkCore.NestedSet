using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>
    /// Empty selected totals retain repeated positions and unknown source identities in requested order.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("anchor")]
    [InlineData("signal")]
    [InlineData("source")]
    public async Task EmptySelectedTotalsPreserveRepeatedOrderedGrain(string foreign)
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = [SignalReportDataset.Id(2), SignalReportDataset.Id(1), SignalReportDataset.Id(2)],
            ChannelIds = [SignalReportDataset.Id(101), SignalReportDataset.Id(101)],
            Sources = [(CapturePort)99, CapturePort.Pulse, (CapturePort)99],
        };

        SignalReportTotal[] totals = foreign == "none" ? [] :
        [
            Total(foreign == "anchor" ? SignalReportDataset.Id(99) : snapshot.AnchorIds[0],
                foreign == "signal" ? SignalReportDataset.Id(199) : snapshot.ChannelIds[0],
                foreign == "source" ? CapturePort.Sweep : snapshot.Sources[0], 13),
        ];

        var expected = snapshot.AnchorIds.SelectMany(anchor => snapshot.ChannelIds.SelectMany(signal =>
            snapshot.Sources.Select(source => new SignalReportCell(anchor, signal, source, false, 0)))).ToArray();

        var probe = new ReaderProbe();

        // Act
        var synchronous = SignalReportValidation.CreateGrid(snapshot, totals);
        var asynchronous = await SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(totals, probe, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(expected, synchronous);
        Assert.Equal(expected, asynchronous);
        Assert.NotSame(synchronous[0], synchronous[2]);
        Assert.NotSame(asynchronous[0], asynchronous[2]);
        Assert.Equal(totals.Length, probe.ReadCount);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
    }

    /// <summary>
    /// Foreign-only input cannot return an empty selected grid before a late reader or disposal failure.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOnlyGridPreservesLateFailureWithoutPartialCells(bool disposal)
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        SignalReportTotal[] totals =
        [
            Total(SignalReportDataset.Id(99), snapshot.ChannelIds[0], CapturePort.Pulse, 7),
        ];

        var failure = new InvalidOperationException("foreign-only completion failure");
        var probe = new ReaderProbe
        {
            ReadFailure = disposal ? null : failure,
            DisposeFailure = disposal ? failure : null,
        };

        SignalReportCell[]? cells = null;

        // Act
        var error = await Record.ExceptionAsync(async () => cells = await SignalReportValidation.CreateGridAsync(
            snapshot, ReadRows(totals, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        Assert.Same(failure, error);
        Assert.Null(cells);
        Assert.Equal(totals.Length, probe.ReadCount);
        Assert.Equal(disposal, probe.EndReached);
        Assert.True(probe.Disposed);
    }
}
