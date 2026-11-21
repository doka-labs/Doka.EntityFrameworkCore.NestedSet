using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures actual request validation and deduplicated same-owner snapshots.</summary>
public class SignalReportSnapshotBenchmarks
{
    private SignalReportRequest? _request;

    /// <summary>Gets or sets the number of independent metadata owner types.</summary>
    [Params(20)]
    public int Types { get; set; } = 20;

    /// <summary>Gets or sets distinct fields for each owner.</summary>
    [Params(4)]
    public int FieldsPerType { get; set; } = 4;

    /// <summary>Prepares the original doubled-occurrence request outside measurement.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Types, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(FieldsPerType, 1);
        var criteria = Enumerable.Range(1, Types)
            .SelectMany(type => Enumerable.Range(1, FieldsPerType).SelectMany(field =>
            {
                var criterion = new TagCriterion(SignalReportDataset.Id(type), $"Field{field}", "choice");

                return new[] { criterion, criterion };
            }))
            .ToArray();

        _request = new SignalReportRequest(
            SignalReportDataset.Id(100),
            SignalReportDataset.Id(101),
            [SignalReportDataset.Id(1)],
            [SignalReportDataset.PulseChannel],
            [CapturePort.Pulse],
            SignalReportDataset.FirstDay,
            SignalReportDataset.FirstDay.AddDays(2),
            MeasurementGranularity.Daily,
            criteria);

        VerifyWorkload();
    }

    /// <summary>Captures the actual immutable request with every original duplicate occurrence.</summary>
    /// <returns>The sample's validated private snapshot.</returns>
    [Benchmark]
    public object Snapshot() => SignalReportValidation.Snapshot(
        _request ?? throw new InvalidOperationException("Prepare the request first."));

    /// <summary>Checks unique owner and field counts independently of numeric measurements.</summary>
    [GlobalCleanup]
    public void VerifyWorkload()
    {
        var result = (ValidatedSignalReport)Snapshot();
        BenchmarkFixture.Require(
            result.Groups.Length == Types
                && result.Groups.All(group => group.Criteria.Length == FieldsPerType),
            "Request snapshot lost owners or deduplicated fields.");
    }
}
