using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

/// <summary>Checks request snapshots independently of SQL providers.</summary>
public sealed class SignalReportSnapshotTests
{
    /// <summary>Owner groups retain every unique criterion while removing duplicate request occurrences.</summary>
    [Fact]
    public void Snapshot_RepeatedCriteria_RetainsEachDistinctCriterionOnce()
    {
        // Arrange
        var criteria = Enumerable
            .Range(1, 20)
            .SelectMany(type => Enumerable
                .Range(1, 4)
                .SelectMany(field =>
                {
                    var criterion = new TagCriterion(SignalReportDataset.Id(type), $"Field{field}", "choice");

                    return new[] { criterion, criterion };
                }))
            .ToArray();

        var request = new SignalReportRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [SignalReportDataset.Id(1)],
            [SignalReportDataset.PulseChannel],
            [CapturePort.Pulse],
            SignalReportDataset.FirstDay,
            SignalReportDataset.FirstDay.AddDays(2),
            MeasurementGranularity.Daily,
            criteria);

        // Act
        var snapshot = SignalReportValidation.Snapshot(request);

        // Assert
        Assert.Equal(20, snapshot.Groups.Length);
        Assert.Equal(80, snapshot.Groups.Sum(group => group.Criteria.Length));
        Assert.All(snapshot.Groups, group => Assert.Equal(4, group.Criteria.Length));
    }
}
