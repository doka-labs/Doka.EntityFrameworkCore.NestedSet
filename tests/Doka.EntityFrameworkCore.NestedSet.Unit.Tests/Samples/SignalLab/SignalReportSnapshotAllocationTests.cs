using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

/// <summary>Measures request snapshots separately from parallel functional validation tests.</summary>
/// <param name="output">Records the complete fixed-profile allocation result.</param>
// WHY: Only the allocation probe needs the existing nonparallel measurement collection; ordinary
// validation tests keep their previous scheduling. Work, warmup and allocation budget are unchanged.
[Collection("Allocation measurements")]
public sealed class SignalReportSnapshotAllocationTests(ITestOutputHelper output)
{
    /// <summary>Owner groups avoid reconstructing independent grouping sets for each scalar field.</summary>
    [Fact]
    public void SnapshotRetainsABoundedPerCriterionAllocation()
    {
        // Arrange
        var criteria = Enumerable.Range(1, 20).SelectMany(type => Enumerable.Range(1, 4)
            .SelectMany(field =>
            {
                var criterion = new TagCriterion(SignalReportDataset.Id(type), $"Field{field}", "choice");

                return new[] { criterion, criterion };
            })).ToArray();

        var request = new SignalReportRequest(Guid.NewGuid(), Guid.NewGuid(), [SignalReportDataset.Id(1)],
            [SignalReportDataset.PulseChannel], [CapturePort.Pulse], SignalReportDataset.FirstDay, SignalReportDataset.FirstDay.AddDays(2),
            MeasurementGranularity.Daily, criteria);

        for (var warmup = 0; warmup < 10; warmup++)
        {
            _ = SignalReportValidation.Snapshot(request);
        }

        // Act
        var allocated = QueryConstructionAllocation.Measure(() => SignalReportValidation.Snapshot(request));

        // Assert
        output.WriteLine($"Snapshot: types=20; unique criteria=80; requested criteria=160; bytes/report={allocated}.");
        Assert.InRange(allocated, 0, 24 * 1024);
    }
}
