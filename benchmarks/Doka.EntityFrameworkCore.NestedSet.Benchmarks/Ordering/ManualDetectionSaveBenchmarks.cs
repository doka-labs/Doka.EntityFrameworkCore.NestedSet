namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures explicit hierarchy work while automatic change detection remains disabled.</summary>
/// <remarks>
/// Unrelated entries remain unchanged so before/after observations perform identical database writes.
/// Rejection and unmarked-payload semantics belong to the functional regression tests, outside timing.
/// </remarks>
public class ManualDetectionSaveBenchmarks : DatabaseBenchmark
{
    private SaveOperation _completed;
    private BenchmarkNode? _node;

    /// <inheritdoc />
    protected override bool Ordered => true;

    /// <summary>Persists an explicitly marked name change and its configured sibling reorder.</summary>
    [Benchmark]
    public async Task MarkedOrder()
    {
        var node = PreparedNode();
        node.Name = "000-first";
        Fixture
            .Context
            .Entry(node)
            .Property(value => value.Name)
            .IsModified = true;

        await Fixture.Context.SaveChangesAsync(CancellationToken.None);
        _completed = SaveOperation.Order;
    }

    /// <summary>Moves an explicitly marked Parent while unrelated tracked payload remains unchanged.</summary>
    [Benchmark]
    public async Task MarkedParent()
    {
        var node = PreparedNode();
        node.ParentId = Shape == BenchmarkShape.Deep
            ? 1
            : 2;

        Fixture
            .Context
            .Entry(node)
            .Property(value => value.ParentId)
            .IsModified = true;

        await Fixture.Context.SaveChangesAsync(CancellationToken.None);
        _completed = SaveOperation.Parent;
    }

    /// <inheritdoc />
    public override async Task PrepareAsync(
        CancellationToken cancellationToken
    )
    {
        _completed = SaveOperation.None;

        // WHY: Setup must not inherit the previous measured iteration's setting while rebuilding the catalog.
        Fixture.Context.ChangeTracker.AutoDetectChangesEnabled = true;
        await base.PrepareAsync(cancellationToken);

        var key = Shape == BenchmarkShape.Deep
            ? Fixture.LeafKey
            : 3;

        _node = await Fixture.Context.Nodes.SingleAsync(value => value.Id == key, cancellationToken);
        Fixture.Context.ChangeTracker.AutoDetectChangesEnabled = false;
    }

    /// <inheritdoc />
    public override async Task VerifyAsync(
        CancellationToken cancellationToken
    )
    {
        BenchmarkFixture.Require(_completed != SaveOperation.None, "The measured save did not execute.");
        BenchmarkFixture.Require(
            !Fixture.Context.ChangeTracker.AutoDetectChangesEnabled,
            "The measured save changed the caller's manual-detection setting.");

        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(cancellationToken) == Nodes,
            "The measured save persisted an unexpected node count.");

        var key = PreparedNode()
            .Id;

        var saved = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == key, cancellationToken);

        BenchmarkFixture.Require(
            _completed == SaveOperation.Parent
                ? saved.ParentId
                == (Shape == BenchmarkShape.Deep
                    ? 1
                    : 2)
                : saved is { Name: "000-first", Position: 0 },
            "The explicitly marked hierarchy change did not persist.");

        await base.VerifyAsync(cancellationToken);
    }

    /// <summary>Requires the unchanged tracked node prepared outside the observation window.</summary>
    /// <returns>The node used by both explicitly marked save cases.</returns>
    private BenchmarkNode PreparedNode() =>
        _node ?? throw new InvalidOperationException("Prepare the tracked node first.");

    /// <summary>Distinguishes actual save effects from a valid-looking benchmark no-op.</summary>
    private enum SaveOperation
    {
        None,
        Order,
        Parent,
    }
}
