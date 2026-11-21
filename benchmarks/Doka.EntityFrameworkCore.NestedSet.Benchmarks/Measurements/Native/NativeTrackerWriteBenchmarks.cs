namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes the original single-child and 131-root imports with baseline and 20,000-entry trackers.</summary>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class NativeTrackerWriteBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private NativeCustomContext? _context;
    private Func<CancellationToken, Task>? _write;
    private NativeCustomNode[]? _inputs;
    private int _detections;
    private int _saves;
    private readonly CommandObservation _observation = new();

    /// <summary>Gets or sets the actual imported topology and unrelated tracker population.</summary>
    [ParamsSource(nameof(Cases))]
    public NativeTrackerWriteCase Case { get; set; } = new(false, 0);

    /// <summary>Gets baseline and crowded versions of both original SQLite tracker workloads.</summary>
    public static IEnumerable<NativeTrackerWriteCase> Cases =>
    [
        new(false, 0), new(false, 20_000), new(true, 0), new(true, 20_000),
    ];

    /// <inheritdoc />
    CommandObservation IDatabaseBenchmark.Observation => _observation;

    /// <summary>Rejects invalid input before acquiring any native catalog.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        ArgumentNullException.ThrowIfNull(Case);
        ArgumentOutOfRangeException.ThrowIfNegative(Case.Tracked);
    }

    /// <summary>
    /// Resets inputs, tracker and independently warmed registry state before each measured operation.
    /// </summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync(CancellationToken.None));

    /// <summary>Invokes exactly one public child insert or complete 131-root forest import.</summary>
    [Benchmark]
    public Task Write() =>
        (_write ?? throw new InvalidOperationException("Prepare the tracker write first."))(CancellationToken.None);

    /// <summary>
    /// Checks complete persisted geometry, tracker ownership and bounded save waves outside measurement.
    /// </summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync(CancellationToken.None));

    /// <inheritdoc />
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await CleanupAsync();
        Initialize();
        var builder = new DbContextOptionsBuilder();
        BenchmarkEnvironment.ConfigureProvider(
            builder,
            BenchmarkRunOptions.Current.Engine,
            BenchmarkEnvironment.ConnectionString);

        if (BenchmarkRunOptions.Current.Diagnostics)
        {
            builder.AddInterceptors(_observation);
        }

        var context = new NativeCustomContext(builder.Options);
        _context = context;
        await context.Database.EnsureDeletedAsync(cancellationToken);

        if (BenchmarkRunOptions.Current.Engine == BenchmarkEngine.SqliteMemory)
        {
            // WHY: SQLite retains this iteration's catalog through its explicitly owned open connection.
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        await context.Database.EnsureCreatedAsync(cancellationToken);
        var hierarchy = context.NestedSet<NativeCustomNode>().ForScope(7);
        await hierarchy.InsertRootAsync(new NativeCustomNode { NodeId = 1_000_001 }, Guid.Empty, cancellationToken);
        context.ChangeTracker.Clear();
        context.AttachRange(Enumerable.Range(1, Case.Tracked).Select(id => new NativeCustomMarker { Id = id }));
        _inputs = Enumerable
            .Range(1, Case.Forest ? 131 : 1)
            .Select(id => new NativeCustomNode { NodeId = id })
            .ToArray();

        if (Case.Forest)
        {
            var imports = _inputs.Select(node => new NestedSetTreeImport<NativeCustomNode, Guid>(
                new Guid(node.NodeId, 0, 0, new byte[8]), new NestedSetBranch<NativeCustomNode>(node))).ToArray();
            _write = token => hierarchy.InsertForestAsync(imports, token);
        }
        else
        {
            _write = token => hierarchy.InsertChildAsync(_inputs[0], 1_000_001, token);
        }

        _detections = 0;
        _saves = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => _detections++;
        context.SavedChanges += (_, _) => _saves++;
        _observation.Reset();
    }

    /// <inheritdoc />
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Prepare the tracker write first.");
        var inputs = _inputs ?? throw new InvalidOperationException("Prepare detached inputs first.");
        BenchmarkFixture.Require(_saves == (Case.Forest ? 3 : 1) && _detections == (Case.Forest ? 4 : 2),
            "Native tracker writing changed its global save/detection waves.");
        BenchmarkFixture.Require(context.ChangeTracker.Entries<NativeCustomMarker>()
            .Count(entry => entry.State == EntityState.Unchanged) == Case.Tracked,
            "Tracker writing changed unrelated payload state.");
        BenchmarkFixture.Require(await context.Nodes.CountAsync(cancellationToken) == inputs.Length + 1,
            "The public tracker write did not persist every requested node.");

        foreach (var input in inputs)
        {
            BenchmarkFixture.Require(context.Entry(input).State == EntityState.Detached,
                "Public insertion retained an input entry.");
            BenchmarkFixture.Require(Case.Forest
                ? input is { Start: 1, End: 2, Depth: 0, Position: 0, Parent: null }
                : input is { Start: 2, End: 3, Depth: 1, Position: 0, Parent: 1_000_001 },
                "The public tracker write produced incorrect input geometry.");
        }
    }

    /// <inheritdoc />
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            _write = null;
            _inputs = null;

            if (_context is { } context)
            {
                _context = null;
                await context.DisposeAsync();
            }
        }
        finally
        {
            // WHY: Finalization suppression belongs to the owned lifecycle even when cleanup fails.
            GC.SuppressFinalize(this);
        }
    }
}
