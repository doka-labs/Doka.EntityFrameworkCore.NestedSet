using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Measures the actual two-reader SignalLab catalog validation and incremental ordered-grid materialization.
/// </summary>
[ThreadingDiagnoser]
public class SignalMaterializationBenchmarks
{
    private SignalMaterializationData? _data;
    private SignalMaterializationRows<SignalCatalogRow>? _catalog;
    private SignalMaterializationRows<SignalReportTotal>? _totals;
    private SignalReportCell[]? _result;
    private long _completed;

    /// <summary>Gets or sets the complete selected anchor/SignalLab/source grain.</summary>
    [Params(24, 6144)]
    public int Cells { get; set; }

    /// <summary>Gets or sets whether zero, two, or all selected groups have persisted values.</summary>
    [Params(SignalMaterializationDensity.Empty, SignalMaterializationDensity.Sparse, SignalMaterializationDensity.Dense)]
    public SignalMaterializationDensity Density { get; set; }

    /// <summary>Gets or sets whether each catalog and totals reader suspends on its first read.</summary>
    [Params(false, true)]
    public bool SuspendFirstRead { get; set; }

    /// <summary>Gets or sets whether incoming totals interleave anchors and channels by measurement source.</summary>
    [Params(false, true)]
    public bool InterleavedSources { get; set; }

    /// <summary>Gets the latest complete result array, without retaining earlier invocation arrays.</summary>
    /// <remarks>The sample projection is internal; object exposes its actual array reference without copying.</remarks>
    public object Result => _result ?? throw new InvalidOperationException("Materialize the prepared SignalLab grid first.");

    /// <summary>Prepares stable inputs and verifies a complete untimed operation before adaptive measurement.</summary>
    /// <returns>Completion after the independent full-cell and reader-counter setup controls pass.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        // WHY: Failed reconfiguration must not leave an earlier valid fixture available under new parameters.
        _data = null;
        _catalog = null;
        _totals = null;
        _result = null;
        _completed = 0;
        var data = SignalMaterializationData.Create(Cells, Density, InterleavedSources);

        PrepareReaders(data);
        await MaterializeAsync();
        await VerifyAsync();
        PrepareReaders(data);
    }

    /// <summary>Executes the actual sample's asynchronous catalog and scalar-grid consumers.</summary>
    /// <returns>The actual grid array reference, including ordered missing cells and measured zeroes.</returns>
    /// <remarks>
    /// The sample's cell projection is internal. The public benchmark returns its existing array as object,
    /// preserving the actual materialization without widening the sample API or allocating a wrapper.
    /// </remarks>
    [Benchmark]
    public async Task<object> MaterializeAsync()
    {
        var data = Data;
        var catalog = _catalog ?? throw new InvalidOperationException("Prepare the catalog reader first.");
        var totals = _totals ?? throw new InvalidOperationException("Prepare the totals reader first.");

        // WHY: A new invocation releases the prior array before materialization. Only its final result is
        // retained for cleanup; no history grows across pilot, warmup, measurement, or diagnostic invocations.
        _result = null;
        await SignalReportValidation.RequireCatalogAsync(data.Request, catalog, CancellationToken.None);
        CancellationToken.None.ThrowIfCancellationRequested();
        _result = await SignalReportValidation.CreateGridAsync(data.Request, totals, CancellationToken.None);
        _completed++;

        return _result;
    }

    /// <summary>
    /// Checks the latest complete grid and every invocation's aggregate reader lifecycle outside timing.
    /// </summary>
    /// <returns>A completed task after verification; this CPU-only check performs no asynchronous I/O.</returns>
    [GlobalCleanup]
    public Task VerifyAsync()
    {
        var data = Data;
        var actual = (SignalReportCell[])Result;

        // WHY: Full-cell checks run only in global setup and cleanup. Timed intermediate results are not
        // individually validated; bounded aggregate counters still require two complete readers per invocation.
        BenchmarkFixture.Require(
            actual.SequenceEqual(data.Expected),
            "The latest materialized grid differs from the independent complete cell oracle.");

        RequireReaderCounts(_catalog, data.Catalog.Length);
        RequireReaderCounts(_totals, data.Totals.Length);

        return Task.CompletedTask;
    }

    /// <summary>Resets only global preparation work, never a measured iteration or invocation.</summary>
    private void PrepareReaders(
        SignalMaterializationData data
    )
    {
        _data = data;
        _catalog = new SignalMaterializationRows<SignalCatalogRow>(data.Catalog, SuspendFirstRead);
        _totals = new SignalMaterializationRows<SignalReportTotal>(data.Totals, SuspendFirstRead);
        _result = null;
        _completed = 0;
    }

    /// <summary>Requires complete EOF, row transport, disposal, and requested suspension counts.</summary>
    private void RequireReaderCounts<T>(
        SignalMaterializationRows<T>? rows,
        int count
    )
    {
        var expectedSuspensions = SuspendFirstRead ? _completed : 0;
        BenchmarkFixture.Require(
            rows is not null
            && rows.Enumerations == _completed
            && rows.Disposals == _completed
            && rows.EndOfStreams == _completed
            && rows.RowsRead == checked(_completed * count)
            && rows.Suspensions == expectedSuspensions,
            "Materialization reader acquisition, row, EOF, disposal, or suspension counts differ.");
    }

    /// <summary>Rejects execution and verification before successful global preparation.</summary>
    private SignalMaterializationData Data =>
        _data ?? throw new InvalidOperationException("Prepare the materialization fixture first.");
}
