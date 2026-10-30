namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes expression construction allocations without creating or reading a database.</summary>
/// <remarks>Selection bounds are logical expression inputs, not a claim about a persisted node population.</remarks>
// WHY: BenchmarkDotNet generates a derived execution type in its isolated child process.
public class CompositionConstructionBenchmarks : IAsyncDisposable
{
    private ManualBenchmarkContext? _context;

    /// <summary>Gets or sets the logical number of selected anchor keys in the expression.</summary>
    [Params(1, 10, 100, 1_000)]
    public int Anchors { get; set; }

    /// <summary>Gets or sets the logical selective payload range encoded as scalar parameters.</summary>
    [Params(1, 100)]
    public int PayloadRows { get; set; }

    /// <summary>Finalizes mapped metadata outside measurement without acquiring any database resource.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        var options = new DbContextOptionsBuilder()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

        _context = new ManualBenchmarkContext(options);
        GC.KeepAlive(_context.Model);
        GC.KeepAlive(Api());
        GC.KeepAlive(Reference());
    }

    /// <summary>Constructs the complete equivalent mapping-specific application-LINQ expression.</summary>
    /// <returns>The deferred query consumed by BenchmarkDotNet, without executing its provider.</returns>
    [Benchmark(Baseline = true)]
    public object Reference() =>
        CompositionBenchmarkQueries.Reference(Context, Guid.Empty, 100_000, Anchors, PayloadRows);

    /// <summary>Constructs the complete generic nested-set composition over the same finalized metadata.</summary>
    /// <returns>The deferred query consumed by BenchmarkDotNet, without opening a connection.</returns>
    [Benchmark]
    public object Api() => CompositionBenchmarkQueries.Api(Context, Guid.Empty, 100_000, Anchors, PayloadRows);

    /// <summary>Releases the unopened context after the isolated measurement process completes.</summary>
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

    /// <summary>Rejects invocation outside the ordinary BenchmarkDotNet initialization lifecycle.</summary>
    private ManualBenchmarkContext Context =>
        _context ?? throw new InvalidOperationException("Initialize the construction benchmark before invoking it.");
}
