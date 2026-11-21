namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes the real clean-context mutation boundary with an unchanged unrelated tracker.</summary>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class CleanMutationBoundaryBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private ManualBenchmarkContext? _context;
    private Func<CancellationToken, Task>? _execute;
    private bool _executed;
    private int _detections;
    private readonly CommandObservation _observation = new();

    /// <summary>Gets or sets the unchanged marker population captured by the original guard scenario.</summary>
    [Params(0, 20_000)]
    public int Tracked { get; set; }

    /// <inheritdoc />
    CommandObservation IDatabaseBenchmark.Observation => _observation;

    /// <summary>Rejects an invalid tracker count before native setup.</summary>
    [GlobalSetup]
    public void Initialize() => ArgumentOutOfRangeException.ThrowIfNegative(Tracked);

    /// <summary>Creates a clean, independently reserved empty tree before each measured invocation.</summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync(CancellationToken.None));

    /// <summary>Executes the actual coordinated no-op boundary without inserting hierarchy nodes.</summary>
    [Benchmark]
    public Task AdmitCleanTracker() =>
        (_execute ?? throw new InvalidOperationException("Prepare the clean boundary first."))(CancellationToken.None);

    /// <summary>Verifies callback execution and detection policy after measurement.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync(CancellationToken.None));

    /// <inheritdoc />
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await CleanupAsync();
        Initialize();
        var options = new DbContextOptionsBuilder();
        BenchmarkEnvironment.ConfigureProvider(
            options,
            BenchmarkRunOptions.Current.Engine,
            BenchmarkEnvironment.ConnectionString);

        if (BenchmarkRunOptions.Current.Diagnostics)
        {
            options.AddInterceptors(_observation);
        }

        var context = new ManualBenchmarkContext(options.Options);
        _context = context;
        await context.Database.EnsureDeletedAsync(cancellationToken);

        if (BenchmarkRunOptions.Current.Engine == BenchmarkEngine.SqliteMemory)
        {
            // WHY: This open owner connection retains the recreated in-memory database throughout the iteration.
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        await context.Database.EnsureCreatedAsync(cancellationToken);
        context.AttachRange(Enumerable.Range(1, Tracked).Select(id => new BenchmarkTrackedEntity { Id = id }));
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        context.ChangeTracker.DetectedAllChanges += (_, _) => _detections++;
        var type = context.Model.FindEntityType(typeof(BenchmarkNode))!;
        var executor = new NestedSetMutationExecutor<BenchmarkNode, int, Guid, int>(context, type);

        // WHY: Reserve once outside measurement; the measured Existing boundary must still scan every clean entry.
        await executor.ExecuteAsync(static _ => Task.CompletedTask,
            [new NestedSetTreeLockRequest<Guid, int>(type, 1, Guid.Empty, NestedSetTreeLockMode.New)],
            cancellationToken);
        var requests = new[]
        {
            new NestedSetTreeLockRequest<Guid, int>(type, 1, Guid.Empty, NestedSetTreeLockMode.Existing),
        };
        _execute = token => executor.ExecuteAsync(_ =>
        {
            _executed = true;

            return Task.CompletedTask;
        }, requests, token);
        _executed = false;
        _detections = 0;
        _observation.Reset();
    }

    /// <inheritdoc />
    public Task VerifyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _context ?? throw new InvalidOperationException("Prepare the clean boundary first.");
        BenchmarkFixture.Require(_executed && _detections == 0,
            "The clean boundary did not preserve manual detection.");
        BenchmarkFixture.Require(
            !context.ChangeTracker.AutoDetectChangesEnabled && context.Database.CurrentTransaction is null,
            "The clean boundary changed policy or retained a transaction.");
        BenchmarkFixture.Require(context.ChangeTracker.Entries<BenchmarkTrackedEntity>().Count() == Tracked,
            "The clean boundary discarded unrelated entries.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            _execute = null;

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
