namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes real indexed fact-selection construction separately from complete read materialization.</summary>
public class AncestorFactConstructionBenchmarks : IAsyncDisposable
{
    private NativeIndexedContext? _context;
    private int[] _selected = [];

    /// <summary>Gets or sets the native dialect used for the opted-in indexed model.</summary>
    [Params(BenchmarkEngine.MySql, BenchmarkEngine.MariaDb)]
    public BenchmarkEngine Engine { get; set; }

    /// <summary>Gets or sets every former captured-fact measurement profile.</summary>
    [ParamsSource(nameof(Cases))]
    public NativeFactCase Case { get; set; } = new("Fact", 100_000, false, false, true);

    /// <summary>Gets the original ordinary, union, missing, inclusive and post-composition consumer cases.</summary>
    public static IEnumerable<NativeFactCase> Cases => NativeFactCase.All;

    /// <summary>Finalizes indexed navigation metadata without creating a database.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        var builder = new DbContextOptionsBuilder<NativeIndexedContext>();
        BenchmarkEnvironment.ConfigureProvider(builder, Engine,
            "Server=127.0.0.1;Database=Construction;User=unused;Password=unused");
        _context = new NativeIndexedContext(builder.Options);
        _selected = NativeFactQueries.Selected(Case);
        GC.KeepAlive(_context.Model);
        GC.KeepAlive(Construct());
    }

    /// <summary>Constructs the entire hierarchy and captured consumer query without invoking its provider.</summary>
    [Benchmark]
    public object Construct() => NativeFactQueries.Build(
        _context ?? throw new InvalidOperationException("Initialize indexed metadata first."), Case, _selected);

    /// <summary>Checks no-I/O ownership and releases the unopened context.</summary>
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
                        "Indexed construction opened a database connection.");
                    BenchmarkFixture.Require(!context.ChangeTracker.Entries().Any(),
                        "Indexed construction tracked an entity.");
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
