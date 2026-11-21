namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Observes native ordered-save refresh with the exact dense, sparse and adjacent tracker populations.
/// </summary>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public class NativeRefreshBenchmarks : IDatabaseBenchmark, IAsyncDisposable
{
    private BenchmarkFixture? _fixture;
    private BenchmarkNode[]? _tracked;
    private int _saved;

    /// <summary>Gets or sets the actual former native refresh workload.</summary>
    [ParamsSource(nameof(Cases))]
    public NativeRefreshCase Case { get; set; } = new(5_000, false, false);

    /// <summary>Gets the three independent native refresh geometries.</summary>
    public static IEnumerable<NativeRefreshCase> Cases =>
    [
        new(5_000, false, false),
        new(100_000, true, false),
        new(5_000, false, true),
    ];

    /// <inheritdoc />
    CommandObservation IDatabaseBenchmark.Observation => Fixture.Counter;

    private BenchmarkFixture Fixture =>
        _fixture ?? throw new InvalidOperationException("Initialize the refresh fixture first.");

    /// <summary>Creates the configured-order context, retaining the original total child population.</summary>
    [GlobalSetup]
    public void Initialize()
    {
        ArgumentNullException.ThrowIfNull(Case);
        ArgumentOutOfRangeException.ThrowIfLessThan(Case.Children, 2);
        _fixture = BenchmarkFixture.Create(new BenchmarkScenario(Case.Children + 1, BenchmarkShape.Wide, 1, 0),
            true, BenchmarkRunOptions.Current, BenchmarkEnvironment.ConnectionString, CancellationToken.None);
    }

    /// <summary>Resets every row and tracker before the single measured invocation.</summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync(CancellationToken.None));

    /// <summary>Renames the first sibling and saves once with caller-owned state acceptance.</summary>
    [Benchmark]
    public async Task SaveAndRefresh()
    {
        var first = (_tracked ?? throw new InvalidOperationException("Prepare the refresh candidates first."))[0];
        first.Name = Case.Adjacent ? "Node-000000003z" : "zzzz";
        _saved = await Fixture.Context.SaveChangesAsync(false, CancellationToken.None);
    }

    /// <summary>Checks persisted effects and complete tracked coordinates after the measured save.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync(CancellationToken.None));

    /// <inheritdoc />
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await Fixture.ResetAsync(true, cancellationToken);
        var query = Fixture.Context.Nodes.Where(node => node.ParentId != null).OrderBy(node => node.Id).AsQueryable();

        if (Case.Sparse)
        {
            query = query.Where(node => node.Id == 2);
        }

        _tracked = await query.ToArrayAsync(cancellationToken);
        _saved = 0;
        Fixture.Counter.Reset();
    }

    /// <inheritdoc />
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var tracked = _tracked ?? throw new InvalidOperationException("Prepare the refresh candidates first.");
        var position = Case.Adjacent ? 1 : Case.Children - 1;
        BenchmarkFixture.Require(_saved == 1 && tracked.Length == (Case.Sparse ? 1 : Case.Children),
            "The native refresh did not save exactly one candidate.");
        BenchmarkFixture.Require(tracked[0].Position == position && tracked[0].Left == position * 2L + 2,
            "The tracked native refresh coordinates are incorrect.");
        BenchmarkFixture.Require(Fixture.Context.Entry(tracked[0]).Property(node => node.Name).IsModified,
            "SaveChanges(false) unexpectedly accepted caller changes.");
        await Fixture.ValidateAsync(cancellationToken);
    }

    /// <inheritdoc />
    [GlobalCleanup]
    public async Task CleanupAsync() => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            _tracked = null;

            if (_fixture is { } fixture)
            {
                _fixture = null;
                await fixture.DisposeAsync();
            }
        }
        finally
        {
            // WHY: Finalization suppression belongs to the owned lifecycle even when cleanup fails.
            GC.SuppressFinalize(this);
        }
    }
}
