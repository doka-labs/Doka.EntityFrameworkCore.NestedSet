using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>Incoming order never substitutes a recently seen anchor, metric or source for a different ID.</summary>
    [Theory]
    [InlineData(4, "forward")]
    [InlineData(4, "reverse")]
    [InlineData(4, "interleaved")]
    [InlineData(9, "forward")]
    [InlineData(9, "reverse")]
    [InlineData(9, "interleaved")]
    public async Task GridAdaptersPreserveIdentityAcrossIncomingOrders(
        int axisSize,
        string ordering
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable.Range(1, axisSize).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(101, axisSize).Select(SignalReportDataset.Id).ToArray(),
            Sources = [CapturePort.Burst, (CapturePort)(-1), CapturePort.Pulse],
        };

        var identities = snapshot.AnchorIds.SelectMany(anchor => snapshot.ChannelIds.SelectMany(signal =>
            snapshot.Sources.Select(source => (Anchor: anchor, Channel: signal, Source: source)))).ToArray();

        var totals = identities.Select((key, index) => Total(key.Anchor, key.Channel, key.Source, index)).ToArray();
        totals = ordering switch
        {
            "reverse" => totals.Reverse().ToArray(),
            "interleaved" => totals.Where((_, index) => index % 2 == 0)
                .Concat(totals.Where((_, index) => index % 2 != 0)).ToArray(),
            _ => totals,
        };

        var expected = identities.Select((key, index) =>
            new SignalReportCell(key.Anchor, key.Channel, key.Source, true, index)).ToArray();

        var probe = new ReaderProbe();

        // Act
        var synchronous = SignalReportValidation.CreateGrid(snapshot, totals);
        var asynchronous = await SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(totals, probe, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(expected, synchronous);
        Assert.Equal(expected, asynchronous);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
    }

    /// <summary>Mixed repeated positions use their first matching identities at both small and larger axes.</summary>
    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(65)]
    public async Task GridAdaptersPreserveMixedRepeatedSelectionPositions(
        int axisSize
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable.Range(0, axisSize).Select(index => SignalReportDataset.Id(index % 3 + 1)).ToArray(),
            ChannelIds = [SignalReportDataset.SweepChannel, SignalReportDataset.PulseChannel, SignalReportDataset.SweepChannel],
            Sources = [(CapturePort)99, CapturePort.Pulse, (CapturePort)99],
        };

        var totals = snapshot.AnchorIds.Distinct().SelectMany(anchor => snapshot.ChannelIds.Distinct().SelectMany(signal =>
            snapshot.Sources.Distinct().Select(source => Total(anchor, signal, source,
                (anchor == snapshot.AnchorIds[0] ? 0 : 13) + (source == CapturePort.Pulse ? 7 : 0)))))
            .Reverse().ToArray();

        var expected = snapshot.AnchorIds.SelectMany(anchor => snapshot.ChannelIds.SelectMany(signal =>
            snapshot.Sources.Select(source => new SignalReportCell(anchor, signal, source, true,
                totals.Single(total => total.AnchorId == anchor && total.ChannelId == signal && total.Source == source).Value))))
            .ToArray();

        var probe = new ReaderProbe();

        // Act
        var synchronous = SignalReportValidation.CreateGrid(snapshot, totals);
        var asynchronous = await SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(totals, probe, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(expected, synchronous);
        Assert.Equal(expected, asynchronous);
        Assert.NotSame(synchronous[0], synchronous[6]);
        Assert.NotSame(asynchronous[0], asynchronous[6]);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
    }

    /// <summary>Large repeated axes preserve ordered output through their membership maps or ordinal buffers.</summary>
    [Theory]
    [InlineData("anchor")]
    [InlineData("signal")]
    [InlineData("source")]
    public async Task GridAdaptersPreserveRepeatedHeapAxisPositions(string axis)
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        snapshot = axis switch
        {
            "anchor" => snapshot with
            {
                AnchorIds = Enumerable.Range(0, 65).Select(index => SignalReportDataset.Id(index % 3 + 1)).ToArray(),
            },
            "signal" => snapshot with
            {
                ChannelIds = Enumerable.Range(0, 65).Select(index => SignalReportDataset.Id(index % 3 + 101)).ToArray(),
            },
            _ => snapshot with
            {
                Sources = Enumerable.Range(0, 65).Select(index => (CapturePort)(index % 3 + 97)).ToArray(),
            },
        };

        var totals = snapshot.AnchorIds.Distinct().SelectMany(anchor => snapshot.ChannelIds.Distinct().SelectMany(signal =>
            snapshot.Sources.Distinct().Select(source => (Anchor: anchor, Channel: signal, Source: source))))
            .Select((key, index) => Total(key.Anchor, key.Channel, key.Source, index)).Reverse().ToArray();

        var expected = snapshot.AnchorIds.SelectMany(anchor => snapshot.ChannelIds.SelectMany(signal =>
            snapshot.Sources.Select(source => new SignalReportCell(anchor, signal, source, true,
                totals.Single(total => total.AnchorId == anchor && total.ChannelId == signal && total.Source == source).Value))))
            .ToArray();

        var probe = new ReaderProbe();

        // Act
        var synchronous = SignalReportValidation.CreateGrid(snapshot, totals);
        var asynchronous = await SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(totals, probe, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(expected, synchronous);
        Assert.Equal(expected, asynchronous);
        Assert.NotSame(synchronous[0], synchronous[3]);
        Assert.NotSame(asynchronous[0], asynchronous[3]);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
    }

    /// <summary>
    /// Completing after a sparse or dense growth boundary never invents a group or loses its last value.
    /// </summary>
    [Theory]
    [InlineData(24, 17)]
    [InlineData(24, 18)]
    [InlineData(24, 24)]
    [InlineData(64, 37)]
    [InlineData(64, 38)]
    [InlineData(64, 64)]
    public async Task GridReaderPreservesRowsAcrossCapacityBoundaries(
        int selected,
        int measured
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable.Range(1, selected).Select(SignalReportDataset.Id).ToArray(),
        };

        var totals = snapshot.AnchorIds.Take(measured)
            .Select((anchor, index) => Total(anchor, snapshot.ChannelIds[0], CapturePort.Pulse, index)).ToArray();

        var expected = snapshot.AnchorIds.Select((anchor, index) => new SignalReportCell(anchor,
            snapshot.ChannelIds[0], CapturePort.Pulse, index < measured, index < measured ? index : 0)).ToArray();

        var probe = new ReaderProbe();

        // Act
        var actual = await SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(totals, probe, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(measured, probe.ReadCount);
    }

    /// <summary>A duplicate at a full boundary remains the first rejection across later foreign duplicates.</summary>
    [Fact]
    public async Task GridReaderRetainsDuplicateIdentityAtCapacityBoundary()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable.Range(1, 24).Select(SignalReportDataset.Id).ToArray(),
        };

        var selected = snapshot.AnchorIds.Take(17)
            .Select((anchor, index) => Total(anchor, snapshot.ChannelIds[0], CapturePort.Pulse, index)).ToArray();

        var foreign = Total(SignalReportDataset.Id(99), snapshot.ChannelIds[0], CapturePort.Pulse, 7);
        var rows = selected.Append(selected[0]).Append(foreign).Append(foreign).ToArray();
        var probe = new ReaderProbe();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Contains(selected[0].AnchorId.ToString(), argument.Message, StringComparison.Ordinal);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
    }
}
