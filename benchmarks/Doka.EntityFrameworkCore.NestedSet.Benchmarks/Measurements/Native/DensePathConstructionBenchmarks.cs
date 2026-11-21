namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes every actual dense-path native construction variant without executing the provider.</summary>
public class DensePathConstructionBenchmarks : IAsyncDisposable
{
    private NativeCustomContext? _context;

    /// <summary>Gets or sets the provider dialect whose actual metadata supplies expression binding.</summary>
    [Params(BenchmarkEngine.MySql, BenchmarkEngine.MariaDb)]
    public BenchmarkEngine Engine { get; set; }

    /// <summary>Gets or sets the complete former capacity expression profile.</summary>
    [ParamsSource(nameof(Cases))]
    public DenseConstructionCase Case { get; set; } = new("DenseOffPath", 100_000, 99_001, false, false);

    /// <summary>Gets the complete original dense chain, fringe and disjoint-forest construction matrix.</summary>
    public static IEnumerable<DenseConstructionCase> Cases
    {
        get
        {
            foreach (var nodes in new[] { 100_000, 1_000_000 })
            {
                yield return new("DenseOffPath", nodes, nodes - 999, false, false);
                yield return new("DenseOnPath", nodes, 1, false, false);
                yield return new("CleanLongPath", nodes, 1, false, false);
            }

            foreach (var name in new[]
                {
                    "DenseCandidatesAccept", "DenseCandidatesReject",
                    "DenseCandidatesPriorAccept", "DenseCandidatesPriorReject",
                })
            {
                yield return new(name, 100_000, 1, false, false);
            }

            yield return new("SingleUpperFringe", 100_000, 99_020, false, false);
            yield return new("SingleLowerFringe", 100_000, 99_020, false, false);
            yield return new("SameBlockLower", 100_000, 99_980, false, false);
            yield return new("UpperIncomingOnly", 100_000, 99_020, false, false);
            yield return new("DenseCandidatesPriorAccept", 100_000, 1, false, true);
            yield return new("DenseCandidatesPriorReject", 100_000, 1, false, true);

            foreach (var name in new[]
                { "DenseOffBranch", "SelectedBranchFailure", "CleanDistinctLower", "RootAboveSiblingFailures" })
            {
                yield return new(name, 100_000, 0, true, false);
                yield return new(name, 100_000, 0, true, true);
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

        return NativeCompositionQueries.Dense(context, Case);
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
