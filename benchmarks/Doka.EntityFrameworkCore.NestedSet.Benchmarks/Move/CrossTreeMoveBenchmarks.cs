namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures subtree transfer between independent coordinate spaces in one application scope.</summary>
public class CrossTreeMoveBenchmarks : DatabaseBenchmark
{
    private bool _moved;

    /// <inheritdoc />
    public override IEnumerable<int> TreeCounts =>
        BenchmarkRunOptions
            .Current
            .TreeCounts
            .Select(count => Math.Max(2, count))
            .Distinct();

    /// <summary>Moves the complete first tree beneath the second root.</summary>
    [Benchmark]
    public async Task RootUnderOtherTree()
    {
        await Fixture.Hierarchy.MoveToAsync(1, Fixture.Forest.RootKeys[1], CancellationToken.None);
        _moved = true;
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _moved = false;
        await base.PrepareAsync();
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        var node = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == 1, CancellationToken.None);

        BenchmarkFixture.Require(
            _moved && node.ParentId == Fixture.Forest.RootKeys[1] && node.TreeId == Fixture.Forest.Imports[1].TreeId,
            "Cross-tree membership did not change.");

        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(CancellationToken.None) == Nodes,
            "Transfer changed the forest size.");

        await base.VerifyAsync();
    }
}
