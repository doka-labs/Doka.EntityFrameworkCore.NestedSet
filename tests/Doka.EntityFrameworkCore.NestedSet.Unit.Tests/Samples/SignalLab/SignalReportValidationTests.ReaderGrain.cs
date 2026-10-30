using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>Large reordered dense and sparse aggregates preserve all independent selection coordinates.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GridReaderPreservesLargeIndependentGrain(
        bool sparse
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request() with
        {
            AnchorIds = Enumerable.Range(1, 64).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(101, 32).Select(SignalReportDataset.Id).ToArray(),
            Sources = [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst],
        });

        var grain = snapshot.AnchorIds.SelectMany(anchor => snapshot.ChannelIds.SelectMany(signal =>
            snapshot.Sources.Select(source => (Anchor: anchor, Channel: signal, Source: source)))).ToArray();

        var rows = grain.Where((_, index) => !sparse || index == 0 || index == grain.Length - 1)
            .Select(key => Total(key.Anchor, key.Channel, key.Source, key == grain[0] ? 0 : 13)).Reverse().ToArray();

        var expected = grain.Select((key, index) => new SignalReportCell(key.Anchor, key.Channel, key.Source,
            !sparse || index == 0 || index == grain.Length - 1,
            index == 0 || (sparse && index != grain.Length - 1) ? 0 : 13)).ToArray();

        var probe = new ReaderProbe();

        // Act
        var actual = await SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
    }

    /// <summary>Direct synchronous-adapter selections retain repeated positions and arbitrary source values.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task GridAdaptersPreserveRepeatedSelectionsAndSourceIdentity(
        int repetitions
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var anchor = snapshot.AnchorIds[0];
        var signal = snapshot.ChannelIds[0];
        var source = (CapturePort)99;
        snapshot = snapshot with
        {
            AnchorIds = Enumerable.Repeat(anchor, repetitions).ToArray(),
            ChannelIds = Enumerable.Repeat(signal, repetitions).ToArray(),
            Sources = Enumerable.Repeat(source, repetitions).ToArray(),
        };

        SignalReportTotal[] rows = [Total(anchor, signal, source, 7)];
        var expected = Enumerable.Repeat(new SignalReportCell(anchor, signal, source, true, 7),
            repetitions * repetitions * repetitions).ToArray();

        var probe = new ReaderProbe();

        // Act
        var synchronous = SignalReportValidation.CreateGrid(snapshot, rows);
        var asynchronous = await SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(expected, synchronous);
        Assert.Equal(expected, asynchronous);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
    }

    /// <summary>Foreign metric and source identities retain the same duplicate rejection as foreign anchors.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GridReaderRejectsUnselectedMetricOrSourceDuplicates(
        bool foreignSource
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var signal = foreignSource ? snapshot.ChannelIds[0] : SignalReportDataset.Id(99);
        var source = foreignSource ? (CapturePort)99 : CapturePort.Pulse;
        var total = Total(snapshot.AnchorIds[0], signal, source, 7);
        SignalReportTotal[] rows = [total, total];
        var probe = new ReaderProbe();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
    }

    /// <summary>The first duplicate remains authoritative across interleaved selected and foreign identities.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GridReaderRetainsFirstDuplicateAcrossIdentityIndexes(
        bool firstForeign
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var selected = Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 7);
        var foreign = Total(SignalReportDataset.Id(99), snapshot.ChannelIds[0], CapturePort.Pulse, 11);
        var first = firstForeign ? foreign : selected;
        var last = firstForeign ? selected : foreign;
        SignalReportTotal[] rows = [selected, foreign, first, last];
        var probe = new ReaderProbe();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Contains(first.AnchorId.ToString(), argument.Message, StringComparison.Ordinal);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
    }

    /// <summary>Late reader or disposal failures still precede overflow without reserving a huge sparse grid.</summary>
    [Theory]
    [InlineData("none")]
    [InlineData("read")]
    [InlineData("dispose")]
    public async Task GridReaderChecksOverflowAfterSuccessfulDisposal(
        string boundary
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable.Range(1, 46_341).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(1, 46_341).Select(SignalReportDataset.Id).ToArray(),
        };

        var failure = new InvalidOperationException("late sparse aggregate failure");
        var probe = new ReaderProbe
        {
            ReadFailure = boundary == "read" ? failure : null,
            DisposeFailure = boundary == "dispose" ? failure : null,
        };

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(Array.Empty<SignalReportTotal>(), probe, CancellationToken.None), CancellationToken.None));

        // Assert
        Assert.True(probe.Disposed);
        if (boundary == "none")
        {
            Assert.IsType<OverflowException>(error);
        }
        else
        {
            Assert.Same(failure, error);
        }
    }
}
