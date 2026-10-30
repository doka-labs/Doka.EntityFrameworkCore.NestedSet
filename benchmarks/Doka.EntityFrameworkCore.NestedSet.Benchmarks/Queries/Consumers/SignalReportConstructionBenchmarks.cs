using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures the real report builder's warmed expression allocations separately from execution.</summary>
public class SignalReportConstructionBenchmarks : IAsyncDisposable
{
    private SignalLabContext? _context;
    private ValidatedSignalReport? _request;

    /// <summary>Gets or sets the logical anchor-selection length; no persisted population is implied.</summary>
    [Params(1, 4, 16)]
    public int Anchors { get; set; }

    /// <summary>Gets or sets whether metadata selects the nearest typed owner or any matching owner.</summary>
    [Params(false, true)]
    public bool NearestMetadata { get; set; }

    /// <summary>Finalizes the actual sample metadata and four scalar criteria without opening a database.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        _context = new SignalLabContext(
            new DbContextOptionsBuilder()
                .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
                .Options);

        GC.KeepAlive(_context.Model);

        var data = new SignalReportDataset(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Enumerable
                .Range(1, Anchors)
                .Select(SignalReportDataset.Id)
                .ToArray(),
            false);

        _request = SignalReportValidation.Snapshot(
            data.Request(NearestMetadata ? TagOwnerMode.Nearest : TagOwnerMode.Any));

        GC.KeepAlive(Api());
        GC.KeepAlive(Reference());
    }

    /// <summary>Builds the actual sample report expression without performing any catalog or fact read.</summary>
    /// <returns>The deferred scalar report.</returns>
    [Benchmark]
    public object Api() => SignalReportBuilder.Build(Context, Request);

    /// <summary>Builds the complete mapping-specific reference with the same domain criteria and sources.</summary>
    /// <returns>The equivalent deferred scalar report.</returns>
    [Benchmark(Baseline = true)]
    public object Reference() => SignalReportReference.Build(Context, Request);

    /// <summary>Releases the unopened metadata context after the isolated benchmark child completes.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
            _context = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Requires completion of ordinary native setup.</summary>
    private SignalLabContext Context => _context ?? throw new InvalidOperationException("Initialize report metadata first.");

    /// <summary>Requires an owned validated request before expression measurement.</summary>
    private ValidatedSignalReport Request =>
        _request ?? throw new InvalidOperationException("Initialize report criteria first.");
}
