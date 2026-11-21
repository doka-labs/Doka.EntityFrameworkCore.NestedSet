using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures complete empty-grid construction at the original 6144-cell presentation grain.</summary>
public class SignalGridConstructionBenchmarks
{
    private ValidatedSignalReport? _request;
    private SignalReportCell[]? _result;

    /// <summary>Creates the exact 64-anchor, 32-channel, three-source grain outside measurement.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        _request = SignalGridMeasurementData.Request(64, 32);
    }

    /// <summary>Creates every ordered missing cell through the actual sample materializer.</summary>
    /// <returns>The complete actual cell array, without a copied or wrapped result.</returns>
    [Benchmark]
    public object CreateGrid()
    {
        _result = SignalReportValidation.CreateGrid(
            _request ?? throw new InvalidOperationException("Prepare the requested grain first."), []);

        return _result;
    }

    /// <summary>Requires the exact complete missing-cell grain outside numeric measurement.</summary>
    [GlobalCleanup]
    public void Verify()
    {
        // WHY: Missing cells carry an exact literal zero; this is not a tolerance rule for measured data.
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        var missing = _result is { Length: 6144 } && _result.All(cell => !cell.HasData && cell.Value == 0);

        BenchmarkFixture.Require(
            missing,
            "Empty-grid construction lost requested cells or introduced measured values.");
    }
}
