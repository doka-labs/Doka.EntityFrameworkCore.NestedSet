using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>
    /// Grid order follows anchor, metric and source selection order independently from SQL aggregate order.
    /// </summary>
    [Fact]
    public void GridPreservesRequestedGrainAndIgnoresForeignTotals()
    {
        // Arrange
        var first = SignalReportDataset.Id(2);
        var second = SignalReportDataset.Id(1);
        var snapshot = SignalReportValidation.Snapshot(Request() with
        {
            AnchorIds = [first, second],
            ChannelIds = [SignalReportDataset.SweepChannel, SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Sweep, CapturePort.Pulse],
        });

        SignalReportTotal[] totals =
        [
            new()
            {
                AnchorId = second,
                ChannelId = SignalReportDataset.PulseChannel,
                Source = CapturePort.Pulse,
                Value = 7,
            },
            new()
            {
                AnchorId = SignalReportDataset.Id(99),
                ChannelId = SignalReportDataset.SweepChannel,
                Source = CapturePort.Sweep,
                Value = 100,
            },
            new()
            {
                AnchorId = first,
                ChannelId = SignalReportDataset.SweepChannel,
                Source = CapturePort.Sweep,
                Value = 0,
            },
        ];

        SignalReportCell[] expected =
        [
            new(first, SignalReportDataset.SweepChannel, CapturePort.Sweep, true, 0),
            new(first, SignalReportDataset.SweepChannel, CapturePort.Pulse, false, 0),
            new(first, SignalReportDataset.PulseChannel, CapturePort.Sweep, false, 0),
            new(first, SignalReportDataset.PulseChannel, CapturePort.Pulse, false, 0),
            new(second, SignalReportDataset.SweepChannel, CapturePort.Sweep, false, 0),
            new(second, SignalReportDataset.SweepChannel, CapturePort.Pulse, false, 0),
            new(second, SignalReportDataset.PulseChannel, CapturePort.Sweep, false, 0),
            new(second, SignalReportDataset.PulseChannel, CapturePort.Pulse, true, 7),
        ];

        // Act
        var cells = SignalReportValidation.CreateGrid(snapshot, totals);

        // Assert
        Assert.Equal(expected, cells);
    }

    /// <summary>Duplicate SQL groups remain an error even for foreign keys or equal scalar values.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GridRejectsDuplicateTotals(bool foreign, bool sameValue)
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var anchor = foreign ? SignalReportDataset.Id(99) : snapshot.AnchorIds[0];
        SignalReportTotal[] totals =
        [
            new()
            {
                AnchorId = anchor,
                ChannelId = snapshot.ChannelIds[0],
                Source = CapturePort.Pulse,
                Value = 7,
            },
            new()
            {
                AnchorId = anchor,
                ChannelId = snapshot.ChannelIds[0],
                Source = CapturePort.Pulse,
                Value = sameValue ? 7 : 11,
            },
        ];

        // Act
        var error = Record.Exception(() => SignalReportValidation.CreateGrid(snapshot, totals));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>
    /// The complete selected result size is checked before allocating or enumerating billions of cells.
    /// </summary>
    [Fact]
    public void GridRejectsCardinalityOverflowBeforeMaterialization()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request()) with
        {
            AnchorIds = Enumerable.Range(1, 46_341).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(1, 46_341).Select(SignalReportDataset.Id).ToArray(),
        };

        // Act
        var error = Record.Exception(() => SignalReportValidation.CreateGrid(snapshot, []));

        // Assert
        Assert.IsType<OverflowException>(error);
    }

    /// <summary>A large display grain preserves its final total, explicit zero and all absent cells.</summary>
    [Fact]
    public void GridBuildsCompleteLargeGrainWithMissingAndZeroGroups()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request() with
        {
            AnchorIds = Enumerable.Range(1, 64).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(101, 32).Select(SignalReportDataset.Id).ToArray(),
            Sources = [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst],
        });

        SignalReportTotal[] totals =
        [
            new()
            {
                AnchorId = SignalReportDataset.Id(64),
                ChannelId = SignalReportDataset.Id(132),
                Source = CapturePort.Burst,
                Value = 13,
            },
            new()
            {
                AnchorId = SignalReportDataset.Id(1),
                ChannelId = SignalReportDataset.Id(101),
                Source = CapturePort.Sweep,
                Value = 0,
            },
        ];

        // Act
        var cells = SignalReportValidation.CreateGrid(snapshot, totals);

        // Assert
        Assert.Equal(64 * 32 * 3, cells.Length);
        Assert.Equal(new SignalReportCell(SignalReportDataset.Id(1), SignalReportDataset.Id(101),
            CapturePort.Sweep, true, 0), cells[0]);
        Assert.Equal(new SignalReportCell(SignalReportDataset.Id(64), SignalReportDataset.Id(132),
            CapturePort.Burst, true, 13), cells[^1]);
        Assert.Equal(2, cells.Count(cell => cell.HasData));
        Assert.All(cells.Where(cell => !cell.HasData), cell => Assert.Equal(0, cell.Value));
    }

    /// <summary>
    /// A large selected grid allocates the final reference array once rather than growing and copying it.
    /// </summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public void GridRetainsABoundedPerCellAllocation()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request() with
        {
            AnchorIds = Enumerable.Range(1, 64).Select(SignalReportDataset.Id).ToArray(),
            ChannelIds = Enumerable.Range(101, 32).Select(SignalReportDataset.Id).ToArray(),
            Sources = [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst],
        });

        const int cells = 64 * 32 * 3;
        for (var warmup = 0; warmup < 10; warmup++)
        {
            _ = SignalReportValidation.CreateGrid(snapshot, []);
        }

        // Act
        var allocated = QueryConstructionAllocation.Measure(() => SignalReportValidation.CreateGrid(snapshot, []));

        // Assert
        output.WriteLine($"Grid: cells={cells}; bytes/report={allocated}; maximum={cells * 76 + 2048}.");
        Assert.InRange(allocated, 0, cells * 76 + 2048);
    }
}
