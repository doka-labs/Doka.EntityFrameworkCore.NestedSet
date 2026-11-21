namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Moves complete native capacity expression construction to observational BenchmarkDotNet runs.</summary>
public class NativeCompositionConstructionBenchmarks : IAsyncDisposable
{
    private NativeCustomContext? _context;

    /// <summary>Gets or sets the provider dialect whose actual metadata supplies expression binding.</summary>
    [Params(BenchmarkEngine.MySql, BenchmarkEngine.MariaDb, BenchmarkEngine.PostgreSql,
        BenchmarkEngine.SqlServer, BenchmarkEngine.SqliteMemory)]
    public BenchmarkEngine Engine { get; set; }

    /// <summary>Gets or sets the complete former capacity expression profile.</summary>
    [ParamsSource(nameof(Cases))]
    public NativeCompositionCase Case { get; set; } = new("Aggregate", 100_000, 1, 1);

    /// <summary>Gets every original anchor, payload, depth, overlap and nearest-accept/reject variant.</summary>
    public static IEnumerable<NativeCompositionCase> Cases
    {
        get
        {
            foreach (var nodes in new[] { 100_000, 1_000_000, 1_000_001, 10_000_000 })
            {
                foreach (var anchors in new[] { 1, 10, 100, 1_000 })
                {
                    foreach (var facts in new[] { 1, 100, nodes })
                    {
                        yield return new("Aggregate", nodes, anchors, facts);
                    }

                    yield return new("Mixed", nodes, anchors, 1);
                }
            }

            foreach (var depth in new[] { 1_000, 10_000, 100_000 })
            {
                foreach (var facts in new[] { 1, 10, 100 })
                {
                    yield return new("Path", depth, 1, facts);
                }

                foreach (var profile in new[]
                    { "NearestAccept", "NearestReject", "RootOwner", "NearestStrictAccept", "NearestStrictReject" })
                {
                    yield return new(profile, depth, 1, 100);
                }

                foreach (var anchors in new[] { 10, 100 })
                {
                    yield return new("Overlap", depth, anchors, 100_000 / depth);
                }
            }
        }
    }

    /// <summary>Finalizes provider metadata outside measurement while retaining a closed connection.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        var options = new DbContextOptionsBuilder();
        BenchmarkEnvironment.ConfigureProvider(options, Engine, Engine == BenchmarkEngine.SqliteMemory
            ? "Data Source=:memory:"
            : Engine == BenchmarkEngine.SqlServer
                ? "Server=127.0.0.1;Database=Construction;User Id=sa;Password=unused;"
                    + "TrustServerCertificate=True"
                : Engine == BenchmarkEngine.PostgreSql
                    ? "Host=127.0.0.1;Database=Construction;Username=unused;Password=unused"
                    : "Server=127.0.0.1;Database=Construction;User=unused;Password=unused");
        _context = new NativeCustomContext(options.Options);
        GC.KeepAlive(_context.Model);
        GC.KeepAlive(Construct());
    }

    /// <summary>Constructs the complete original native query; it performs no I/O or materialization.</summary>
    [Benchmark]
    public object Construct()
    {
        var context = _context ?? throw new InvalidOperationException("Initialize construction metadata first.");

        return Case.Profile switch
        {
            "Aggregate" => NativeCompositionQueries.Aggregate(
                context, Case.Nodes - Case.Anchors + 2, Case.Nodes - Case.Facts + 1),
            "Mixed" => NativeCompositionQueries.Mixed(context, Case.Nodes - Case.Anchors + 1, Case.Nodes),
            _ => NativeCompositionQueries.Deep(context, Case.Nodes, Case.Facts, Case.Anchors, Case.Profile),
        };
    }

    /// <summary>Checks deferred ownership and releases the unopened context independently of cancellation.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_context is { } context)
            {
                _context = null;

                try
                {
                    BenchmarkFixture.Require(
                        context.Database.GetDbConnection().State == System.Data.ConnectionState.Closed,
                        "Construction unexpectedly opened a database connection.");
                    BenchmarkFixture.Require(!context.ChangeTracker.Entries().Any(),
                        "Construction materialized tracked entities.");
                }
                finally
                {
                    await context.DisposeAsync();
                }
            }
        }
        finally
        {
            // WHY: Finalization suppression belongs to the owned lifecycle even when cleanup fails.
            GC.SuppressFinalize(this);
        }
    }
}
