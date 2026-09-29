namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Measures automatic reordering through SaveChangesAsync with a hierarchy entry preloaded into the tracker.
/// </summary>
public class OrderingSaveBenchmarks : DatabaseBenchmark
{
    private BenchmarkNode? _renamed;
    private int _key;
    private int _expected;

    /// <inheritdoc />
    protected override bool Ordered => true;

    /// <summary>Renames a preloaded tracked node and saves its payload plus automatic hierarchy reorder.</summary>
    /// <remarks>A deep chain has no sibling reorder; it measures the configured save and payload update.</remarks>
    [Benchmark]
    public async Task RenameAndSave()
    {
        var renamed = _renamed ?? throw new InvalidOperationException("Prepare the tracked rename first.");

        _key = renamed.Id;
        _expected = Nodes;
        renamed.Name = "000-first";
        await Fixture.Context.SaveChangesAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _key = 0;
        await base.PrepareAsync();

        // WHY: Node 3 has a preceding sibling in wide/balanced trees; their rename must perform a real reorder.
        var renameKey = Shape == BenchmarkShape.Deep ? Fixture.LeafKey : 3;
        _renamed = await Fixture.Context.Nodes.SingleAsync(node => node.Id == renameKey, CancellationToken.None);
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        var node = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == _key, CancellationToken.None);

        BenchmarkFixture.Require(node is { Name: "000-first", Position: 0 }, "Configured ordering did not occur.");
        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(CancellationToken.None) == _expected,
            "Ordering changed the node count.");

        await base.VerifyAsync();
    }
}
