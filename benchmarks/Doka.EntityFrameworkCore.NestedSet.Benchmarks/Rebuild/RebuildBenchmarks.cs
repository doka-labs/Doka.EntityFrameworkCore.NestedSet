namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures actual repair work from valid adjacency and deliberately damaged derived coordinates.</summary>
public class RebuildBenchmarks : DatabaseBenchmark
{
    private bool _rebuilt;

    /// <summary>Rebuilds every independent tree through the public atomic repair API.</summary>
    [Benchmark]
    public async Task RepairForest()
    {
        foreach (var import in Fixture.Forest.Imports)
        {
            await Fixture
                .Hierarchy
                .InTree(import.TreeId)
                .RebuildAsync(CancellationToken.None);
        }

        _rebuilt = true;
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _rebuilt = false;
        await base.PrepareAsync();

        // WHY: Preserve adjacency and local CHECK invariants while forcing real bounds, depth and position repair.
        await Fixture.Context.Nodes.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(node => node.Left, node => node.Left + (Nodes * 2L))
                .SetProperty(node => node.Right, node => node.Right + (Nodes * 2L))
                .SetProperty(node => node.Depth, node => node.Depth + 1)
                .SetProperty(node => node.Position, node => node.Position + 2),
            CancellationToken.None);

        Fixture.Counter.Reset();
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        var roots = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .Where(node => node.ParentId == null)
            .ToListAsync(CancellationToken.None);

        BenchmarkFixture.Require(
            _rebuilt && roots.Count == Trees && roots.All(node => node is { Left: 1, Depth: 0, Position: 0 }),
            "Rebuild remained a no-op.");

        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(CancellationToken.None) == Nodes,
            "Repair changed the forest size.");

        await base.VerifyAsync();
    }
}
