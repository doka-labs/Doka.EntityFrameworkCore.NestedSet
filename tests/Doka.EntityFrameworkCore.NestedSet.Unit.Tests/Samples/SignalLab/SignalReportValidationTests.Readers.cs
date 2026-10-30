using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>The async catalog retains reordered, duplicate, ignored-kind and exact scalar semantics.</summary>
    [Fact]
    public async Task CatalogReaderMatchesTheCompleteSynchronousCatalog()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var rows = Catalog(snapshot).Reverse().Concat(Catalog(snapshot)).Append(new SignalCatalogRow
        {
            Kind = 99,
            UnitCode = "ignored",
        }).ToArray();

        var probe = new ReaderProbe();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.RequireCatalogAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        Assert.Null(error);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
    }

    /// <summary>All catalog evidence is consumed and disposed before authorization, unit or choice rejection.</summary>
    [Theory]
    [InlineData("authorization", "A selected anchor or channel is missing")]
    [InlineData("unit", "One additive report requires")]
    [InlineData("metadata", "Unknown field, owner classification")]
    public async Task CatalogReaderPreservesRejectionOrderAfterDisposal(
        string failure,
        string expected
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var rows = Catalog(snapshot).Where(row => failure != "authorization" || row.Kind != 0)
            .Where(row => failure != "metadata" || row.Kind != 3)
            .Append(new SignalCatalogRow
            {
                Kind = 1,
                Id = snapshot.ChannelIds[0],
                UnitCode = failure == "metadata" ? "count" : "COUNT",
            }).ToArray();

        var probe = new ReaderProbe();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.RequireCatalogAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.StartsWith(expected, argument.Message, StringComparison.Ordinal);
        Assert.Equal("request", argument.ParamName);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
    }

    /// <summary>A failed or canceled reader wins over invalid catalog evidence already seen.</summary>
    [Theory]
    [InlineData("read")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    public async Task CatalogReaderPreservesLateReaderFailure(
        string boundary
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("catalog reader failure");
        var probe = new ReaderProbe
        {
            ReadFailure = boundary == "read" ? failure : null,
            DisposeFailure = boundary == "dispose" ? failure : null,
            CancelAfterRows = boundary == "cancel" ? cancellation : null,
        };

        SignalCatalogRow[] rows = [new() { Kind = 99 }];

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.RequireCatalogAsync(snapshot,
            ReadRows(rows, probe, cancellation.Token), cancellation.Token));

        // Assert
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
        if (boundary == "cancel")
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.True(cancellation.IsCancellationRequested);
        }
        else
        {
            Assert.Same(failure, error);
        }
    }

    /// <summary>The streamed grid preserves exact tuple identity, order, zero and sparse absence.</summary>
    [Fact]
    public async Task GridReaderMatchesTheCompleteSynchronousGrid()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request() with
        {
            AnchorIds = [SignalReportDataset.Id(2), SignalReportDataset.Id(1)],
            ChannelIds = [SignalReportDataset.SweepChannel, SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Sweep, CapturePort.Pulse],
        });

        SignalReportTotal[] rows =
        [
            Total(snapshot.AnchorIds[1], snapshot.ChannelIds[1], CapturePort.Pulse, 7),
            Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Sweep, 0),
            Total(SignalReportDataset.Id(99), snapshot.ChannelIds[1], CapturePort.Pulse, 100),
            Total(snapshot.AnchorIds[0], snapshot.ChannelIds[1], CapturePort.Sweep, 13),
        ];

        var expected = SignalReportValidation.CreateGrid(snapshot, rows);
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

    /// <summary>Duplicate groups are rejected after EOF even when equal or outside the requested grain.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GridReaderRejectsAllDuplicateGroupsAfterDisposal(
        bool foreign,
        bool sameValue
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var anchor = foreign ? SignalReportDataset.Id(99) : snapshot.AnchorIds[0];
        SignalReportTotal[] rows =
        [
            Total(anchor, snapshot.ChannelIds[0], CapturePort.Pulse, 7),
            Total(anchor, snapshot.ChannelIds[0], CapturePort.Pulse, sameValue ? 7 : 11),
            Total(SignalReportDataset.Id(98), snapshot.ChannelIds[0], CapturePort.Pulse, 19),
        ];

        var probe = new ReaderProbe();
        SignalReportCell[]? cells = null;

        // Act
        var error = await Record.ExceptionAsync(async () => cells = await SignalReportValidation.CreateGridAsync(
            snapshot, ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Null(cells);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
    }

    /// <summary>Late read, cancellation or disposal failures take precedence over an earlier duplicate.</summary>
    [Theory]
    [InlineData("read")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    public async Task GridReaderPreservesLateFailureAfterDuplicate(
        string boundary
    )
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("aggregate reader failure");
        var probe = new ReaderProbe
        {
            ReadFailure = boundary == "read" ? failure : null,
            DisposeFailure = boundary == "dispose" ? failure : null,
            CancelAfterRows = boundary == "cancel" ? cancellation : null,
        };

        SignalReportTotal[] rows =
        [
            Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 7),
            Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 7),
        ];

        SignalReportCell[]? cells = null;

        // Act
        var error = await Record.ExceptionAsync(async () => cells = await SignalReportValidation.CreateGridAsync(
            snapshot, ReadRows(rows, probe, cancellation.Token), cancellation.Token));

        // Assert
        Assert.Null(cells);
        Assert.True(probe.Disposed);
        Assert.Equal(rows.Length, probe.ReadCount);
        if (boundary == "cancel")
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.True(cancellation.IsCancellationRequested);
        }
        else
        {
            Assert.Same(failure, error);
        }
    }

    /// <summary>A duplicate must win over selected-grid overflow, without allocating a partial grid.</summary>
    [Fact]
    public async Task GridReaderRejectsDuplicateBeforeCardinalityOverflow()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable.Range(1, 46_341).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(1, 46_341).Select(SignalReportDataset.Id).ToArray(),
        };

        SignalReportTotal[] rows =
        [
            Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 7),
            Total(snapshot.AnchorIds[0], snapshot.ChannelIds[0], CapturePort.Pulse, 11),
        ];

        var probe = new ReaderProbe();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportValidation.CreateGridAsync(snapshot,
            ReadRows(rows, probe, CancellationToken.None), CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.True(probe.EndReached);
        Assert.True(probe.Disposed);
    }

    /// <summary>Creates independent scalar rows without invoking the grid implementation.</summary>
    private static SignalReportTotal Total(
        Guid anchor,
        Guid signal,
        CapturePort source,
        double value
    ) => new() { AnchorId = anchor, ChannelId = signal, Source = source, Value = value };
}
