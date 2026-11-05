namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures facade-planned inserts while automatic change detection remains disabled.</summary>
/// <remarks>No existing hierarchy node is tracked; explicit mutations retain their normal tracking policy.</remarks>
public class ManualDetectionInsertBenchmarks : DatabaseBenchmark
{
    private BenchmarkForest? _branch;
    private NestedSetTreeImport<BenchmarkNode, Guid>[]? _imports;
    private InsertOperation _completed;

    /// <inheritdoc />
    protected override bool Ordered => true;

    /// <summary>Persists a facade-planned child with manual change detection.</summary>
    [Benchmark]
    public async Task SingleChild()
    {
        await Fixture.Hierarchy.InsertChildAsync(
            new BenchmarkNode
            {
                Id = Nodes + 1,
                Name = "zz-inserted",
                Payload = "inserted",
            },
            1,
            CancellationToken.None);

        _completed = InsertOperation.Single;
    }

    /// <summary>Persists a prebuilt ten-node subtree with manual change detection.</summary>
    [Benchmark]
    public async Task Subtree()
    {
        var branch = _branch ?? throw new InvalidOperationException("Prepare the detached branch first.");
        await Fixture.Hierarchy.InsertSubtreeAsync(branch.Imports[0].Root, 1, CancellationToken.None);
        _completed = InsertOperation.Subtree;
    }

    /// <summary>Persists a prebuilt ten-node independent tree with manual change detection.</summary>
    [Benchmark]
    public async Task Forest()
    {
        var imports = _imports ?? throw new InvalidOperationException("Prepare the detached forest first.");
        await Fixture.Hierarchy.InsertForestAsync(imports, CancellationToken.None);
        _completed = InsertOperation.Forest;
    }

    /// <inheritdoc />
    public override async Task PrepareAsync(CancellationToken cancellationToken)
    {
        _completed = InsertOperation.None;

        // WHY: Setup must not inherit the previous measured iteration's setting while rebuilding the catalog.
        Fixture.Context.ChangeTracker.AutoDetectChangesEnabled = true;
        await base.PrepareAsync(cancellationToken);

        _branch = BenchmarkForest.Build(new BenchmarkScenario(10, BenchmarkShape.Balanced, 1, 0), Nodes);
        _imports =
        [
            new NestedSetTreeImport<BenchmarkNode, Guid>(new Guid(-1, 0, 0, new byte[8]), _branch.Imports[0].Root),
        ];

        Fixture.Context.ChangeTracker.AutoDetectChangesEnabled = false;
    }

    /// <inheritdoc />
    public override async Task VerifyAsync(CancellationToken cancellationToken)
    {
        BenchmarkFixture.Require(_completed != InsertOperation.None, "The measured insertion did not execute.");
        BenchmarkFixture.Require(
            !Fixture.Context.ChangeTracker.AutoDetectChangesEnabled,
            "The measured insertion changed the caller's manual-detection setting.");

        var expectedCount = Nodes + (_completed == InsertOperation.Single ? 1 : 10);

        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(cancellationToken) == expectedCount,
            "The measured insertion persisted an unexpected node count.");

        var saved = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == Nodes + 1, cancellationToken);

        BenchmarkFixture.Require(
            _completed == InsertOperation.Forest ? saved.ParentId is null : saved.ParentId == 1,
            "The facade-planned insertion escaped its destination.");

        await base.VerifyAsync(cancellationToken);
    }

    /// <summary>Distinguishes actual insert effects from a valid-looking benchmark no-op.</summary>
    private enum InsertOperation
    {
        None,
        Single,
        Subtree,
        Forest,
    }
}
