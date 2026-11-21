using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures the real report builder's warmed expression allocations separately from execution.</summary>
public class SignalReportConstructionBenchmarks : IAsyncDisposable
{
    private SignalLabContext? _context;
    private ValidatedSignalReport? _request;
    private ConsumerMetadataServices<SignalLabContext>? _services;

    /// <summary>Gets or sets the actual provider dialect used for disconnected expression construction.</summary>
    [Params("MySql", "MariaDb", "PostgreSql", "SqlServer", "Sqlite")]
    public string Engine { get; set; } = "Sqlite";

    /// <summary>Gets or sets the four-criterion baseline or one added same-owner criterion.</summary>
    [Params(4, 5)]
    public int Criteria { get; set; } = 4;

    /// <summary>Gets or sets the logical anchor-selection length; no persisted population is implied.</summary>
    [Params(1, 4, 16)]
    public int Anchors { get; set; }

    /// <summary>Gets or sets whether metadata selects the nearest typed owner or any matching owner.</summary>
    [Params(false, true)]
    public bool NearestMetadata { get; set; }

    /// <summary>Requires completion of ordinary native setup.</summary>
    private SignalLabContext Context =>
        _context ?? throw new InvalidOperationException("Initialize report metadata first.");

    /// <summary>Requires an owned validated request before expression measurement.</summary>
    private ValidatedSignalReport Request =>
        _request ?? throw new InvalidOperationException("Initialize report criteria first.");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var context = _context;
        var services = _services;
        _context = null;
        _services = null;
        if (services is not null)
        {
            await services.DisposeContextAsync(context);
        }
        else if (context is not null)
        {
            await context.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Finalizes the actual sample metadata and four scalar criteria without opening a database.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        if (Anchors < 1 || Criteria is not (4 or 5))
        {
            throw new ArgumentOutOfRangeException(nameof(Criteria), "Select positive anchors and four or five criteria.");
        }

        _services = ConsumerMetadataServices<SignalLabContext>.Create(Engine);
        _context = new SignalLabContext(_services.Options);

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
            data.Request(
                NearestMetadata
                    ? TagOwnerMode.Nearest
                    : TagOwnerMode.Any));

        if (Criteria == 5)
        {
            var groups = _request.Groups.ToArray();
            var sensor = groups[^1];
            groups[^1] = sensor with
            {
                Criteria =
                [
                    .. sensor.Criteria,
                    new TagCriterion(SignalReportDataset.SensorKind, "CaptureMode", "Continuous", sensor.Mode),
                ],
            };

            _request = _request with { Groups = groups };
        }

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
}
