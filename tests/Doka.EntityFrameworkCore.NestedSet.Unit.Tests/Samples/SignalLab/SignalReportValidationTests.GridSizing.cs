using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>Offset specialization never changes duplicate-before-overflow precedence at either product.</summary>
    [Theory]
    [InlineData("prefix", false)]
    [InlineData("prefix", true)]
    [InlineData("sources", false)]
    [InlineData("sources", true)]
    [InlineData("zero-sources", false)]
    [InlineData("zero-sources", true)]
    public async Task GridReaderDefersAllCardinalityOverflowShapes(
        string shape,
        bool duplicate
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var count = shape == "sources" ? 32_768 : 46_341;
        snapshot = snapshot with
        {
            AnchorIds = Enumerable.Range(1, count).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(1, count).Select(SignalReportDataset.Id).ToArray(),
            Sources = shape switch
            {
                "sources" => [CapturePort.Pulse, CapturePort.Sweep, CapturePort.Burst],
                "zero-sources" => [],
                _ => [CapturePort.Pulse],
            },
        };

        var total = Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 7);
        SignalReportTotal[] rows = duplicate ? [total, total] : [total];
        var probe = new ReaderProbe();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
        if (duplicate)
        {
            Assert.IsType<ArgumentException>(error);
        }
        else
        {
            Assert.IsType<OverflowException>(error);
        }
    }

    /// <summary>Empty dimensions produce the same empty result without rejecting foreign aggregate rows.</summary>
    [Theory]
    [InlineData("anchors")]
    [InlineData("signals")]
    [InlineData("sources")]
    public async Task GridAdaptersPreserveEmptySelectionDimensions(
        string dimension
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var total = Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 7);
        snapshot = snapshot with
        {
            AnchorIds = dimension == "anchors" ? [] : snapshot.AnchorIds,
            ChannelIds = dimension == "signals" ? [] : snapshot.ChannelIds,
            Sources = dimension == "sources" ? [] : snapshot.Sources,
        };

        SignalReportTotal[] rows = [total];
        var probe = new ReaderProbe();

        // Act
        var synchronous = SignalReportValidation.CreateGrid(snapshot, rows);
        var asynchronous = await SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Empty(synchronous);
        Assert.Empty(asynchronous);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
    }
}
