using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>
    /// Selections, owner groups and scalar fields keep their first-requested order after deduplication.
    /// </summary>
    [Fact]
    public void SnapshotPreservesFirstSelectionAndCriterionOrder()
    {
        // Arrange
        var first = SignalReportDataset.Id(2);
        var second = SignalReportDataset.Id(1);
        var band = new TagCriterion(SignalReportDataset.ArrayKind, "Band", "Narrow");
        var rackCode = new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "R9",
            TagOwnerMode.Nearest);

        var carrier = band with
        {
            FieldCode = "Carrier",
            Value = "Omega",
        };

        var cooling = rackCode with
        {
            FieldCode = "Cooling",
            Value = "Liquid",
        };

        var request = Request() with
        {
            AnchorIds = [first, second, first],
            ChannelIds = [SignalReportDataset.SweepChannel, SignalReportDataset.PulseChannel, SignalReportDataset.SweepChannel],
            Sources = [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Sweep, CapturePort.Burst],
            Criteria = [rackCode, band, cooling, carrier, rackCode, band],
        };

        // Act
        var snapshot = SignalReportValidation.Snapshot(request);

        // Assert
        Assert.Equal([first, second], snapshot.AnchorIds);
        Assert.Equal([SignalReportDataset.SweepChannel, SignalReportDataset.PulseChannel], snapshot.ChannelIds);
        Assert.Equal([CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst], snapshot.Sources);
        Assert.Equal([SignalReportDataset.SensorKind, SignalReportDataset.ArrayKind],
            snapshot.Groups.Select(group => group.TypeId));
        Assert.Equal([rackCode, cooling], snapshot.Groups[0].Criteria);
        Assert.Equal([band, carrier], snapshot.Groups[1].Criteria);
        Assert.Equal(TagOwnerMode.Nearest, snapshot.Groups[0].Mode);
        Assert.Equal(TagOwnerMode.Any, snapshot.Groups[1].Mode);
    }

    /// <summary>
    /// Deferred report selections own their arrays even when a caller reuses and mutates every input buffer.
    /// </summary>
    [Fact]
    public void SnapshotOwnsAllCallerSelectionBuffers()
    {
        // Arrange
        Guid[] anchors = [SignalReportDataset.Id(1)];
        Guid[] signals = [SignalReportDataset.PulseChannel];
        CapturePort[] sources = [CapturePort.Pulse];
        var criterion = new TagCriterion(SignalReportDataset.ArrayKind, "Band", "Narrow");
        TagCriterion[] criteria = [criterion];
        var request = Request() with
        {
            AnchorIds = anchors,
            ChannelIds = signals,
            Sources = sources,
            Criteria = criteria,
        };

        var snapshot = SignalReportValidation.Snapshot(request);

        // Act
        anchors[0] = SignalReportDataset.Id(99);
        signals[0] = SignalReportDataset.SweepChannel;
        sources[0] = CapturePort.Sweep;
        criteria[0] = criterion with { Value = "Wide" };

        // Assert
        Assert.Equal(SignalReportDataset.Id(1), Assert.Single(snapshot.AnchorIds));
        Assert.Equal(SignalReportDataset.PulseChannel, Assert.Single(snapshot.ChannelIds));
        Assert.Equal(CapturePort.Pulse, Assert.Single(snapshot.Sources));
        Assert.Same(criterion, Assert.Single(Assert.Single(snapshot.Groups).Criteria));
        Assert.NotSame(anchors, snapshot.AnchorIds);
        Assert.NotSame(signals, snapshot.ChannelIds);
        Assert.NotSame(sources, snapshot.Sources);
        Assert.NotSame(criteria, snapshot.Groups[0].Criteria);
    }

    /// <summary>
    /// Exact field-code identity neither trims padding nor folds case while grouping scalar criteria.
    /// </summary>
    [Fact]
    public void SnapshotKeepsOrdinalFieldCodesDistinct()
    {
        // Arrange
        var criterion = Request().Criteria[0];
        var criteria = new[]
        {
            criterion,
            criterion with
            {
                FieldCode = "band",
                Value = "Wide",
            },
            criterion with
            {
                FieldCode = "Band ",
                Value = "Broad",
            },
        };

        var request = Request() with { Criteria = criteria };

        // Act
        var snapshot = SignalReportValidation.Snapshot(request);

        // Assert
        Assert.Equal(criteria, Assert.Single(snapshot.Groups).Criteria);
    }

    /// <summary>
    /// Equal text from separate criterion instances deduplicates without replacing the first selection.
    /// </summary>
    [Fact]
    public void SnapshotDeduplicatesSeparateOrdinallyEqualCriteria()
    {
        // Arrange
        var first = Request().Criteria[0];
        var duplicate = first with { Value = new string(first.Value.AsSpan()) };
        var request = Request() with { Criteria = [first, duplicate] };

        // Act
        var snapshot = SignalReportValidation.Snapshot(request);

        // Assert
        Assert.Same(first, Assert.Single(Assert.Single(snapshot.Groups).Criteria));
    }

    /// <summary>Null criteria and unsupported owner/classification/scalar forms fail before any catalog read.</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("type")]
    [InlineData("mode")]
    [InlineData("field")]
    [InlineData("value")]
    public void SnapshotRejectsMalformedCriteria(string shape)
    {
        // Arrange
        var criterion = Request().Criteria[0];
        var request = Request() with
        {
            Criteria = [shape switch
            {
                "null" => null!,
                "type" => criterion with { TypeId = Guid.Empty },
                "mode" => criterion with { OwnerMode = (TagOwnerMode)17 },
                "field" => criterion with { FieldCode = string.Empty },
                _ => criterion with { Value = null! },
            }],
        };

        // Act
        var error = Record.Exception(() => SignalReportValidation.Snapshot(request));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Equal("request", argument.ParamName);
    }

}
