namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Defines one-operation iterations with independently prepared state and untimed effect verification.
/// </summary>
[Config(typeof(DatabaseBenchmarkConfiguration))]
public abstract class DatabaseBenchmark
{
    private BenchmarkFixture? _fixture;

    /// <summary>Gets or sets the total persisted or imported node count.</summary>
    [ParamsSource(nameof(NodeCounts))]
    public int Nodes { get; set; }

    /// <summary>Gets or sets the deterministic topology.</summary>
    [ParamsSource(nameof(Shapes))]
    public BenchmarkShape Shape { get; set; }

    /// <summary>Gets or sets the number of independent trees.</summary>
    [ParamsSource(nameof(TreeCounts))]
    public int Trees { get; set; }

    /// <summary>Gets or sets the unrelated unchanged tracker population.</summary>
    [ParamsSource(nameof(TrackedCounts))]
    public int Tracked { get; set; }

    /// <summary>Gets the explicitly selected input sizes.</summary>
    public static IEnumerable<int> NodeCounts => BenchmarkRunOptions.Current.NodeCounts;

    /// <summary>Gets the explicitly selected topologies.</summary>
    public static IEnumerable<BenchmarkShape> Shapes => BenchmarkRunOptions.Current.Shapes;

    /// <summary>Gets the selected number of trees.</summary>
    public virtual IEnumerable<int> TreeCounts => BenchmarkRunOptions.Current.TreeCounts;

    /// <summary>Gets the selected tracker populations.</summary>
    public static IEnumerable<int> TrackedCounts => BenchmarkRunOptions.Current.TrackedCounts;

    /// <summary>Gets whether this family uses configured sibling order.</summary>
    protected virtual bool Ordered => false;

    /// <summary>Gets whether preparation inserts the forest before the measured operation.</summary>
    protected virtual bool Seed => true;

    /// <summary>Gets the initialized fixture only after the public lifecycle has acquired it.</summary>
    internal BenchmarkFixture Fixture =>
        _fixture ?? throw new InvalidOperationException("Initialize the benchmark first.");

    /// <summary>Creates the provider configuration and fixture in the isolated benchmark child.</summary>
    /// <remarks>Database creation and seeding complete in iteration preparation.</remarks>
    [GlobalSetup]
    public void Initialize()
    {
        _fixture = BenchmarkFixture.Create(
            new BenchmarkScenario(Nodes, Shape, Trees, Tracked),
            Ordered,
            BenchmarkRunOptions.Current,
            BenchmarkEnvironment.ConnectionString,
            CancellationToken.None);
    }

    /// <summary>Fully completes asynchronous setup before BDN starts timing or allocation observation.</summary>
    [IterationSetup]
    public void Prepare() => BenchmarkLifecycle.Complete(PrepareAsync());

    /// <summary>Fully completes result verification after BDN finishes timing and allocation observation.</summary>
    [IterationCleanup]
    public void Verify() => BenchmarkLifecycle.Complete(VerifyAsync());

    /// <summary>Prepares fresh state; diagnostic and functional test execution uses the same lifecycle.</summary>
    public virtual Task PrepareAsync() => Fixture.ResetAsync(Seed, CancellationToken.None);

    /// <summary>Validates every remaining tree; feature families add independent effect assertions.</summary>
    public virtual Task VerifyAsync() => Fixture.ValidateAsync(CancellationToken.None);

    /// <summary>Releases the child-owned context after all iterations and diagnostic passes.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
            _fixture = null;
        }
    }

    /// <summary>Gets diagnostic counters after an operation but before verification reads.</summary>
    internal CommandObservation Observation => Fixture.Counter;
}
