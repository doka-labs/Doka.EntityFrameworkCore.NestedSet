namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures single-leaf and shape-dependent subtree movement within one tree.</summary>
public class MoveBenchmarks : DatabaseBenchmark
{
    private int _moved;

    /// <summary>Moves one leaf before the first existing child.</summary>
    [Benchmark]
    public async Task LeafBeforeFirstChild()
    {
        _moved = Fixture.LeafKey;
        await Fixture.Hierarchy.MoveBeforeAsync(_moved, 2, CancellationToken.None);
    }

    /// <summary>Moves the branch rooted at node three before node two.</summary>
    /// <remarks>
    /// The moved branch is one node in Wide, half a binary tree in Balanced, and nearly the entire chain in Deep.
    /// </remarks>
    [Benchmark]
    public async Task BranchBeforeFirstChild()
    {
        _moved = 3;
        await Fixture.Hierarchy.MoveBeforeAsync(_moved, 2, CancellationToken.None);
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _moved = 0;
        await base.PrepareAsync();
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        var node = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == _moved, CancellationToken.None);

        BenchmarkFixture.Require(
            node is { ParentId: 1, Left: 2, Position: 0 },
            "Move remained a no-op or used the wrong placement.");

        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(CancellationToken.None) == Nodes,
            "Move changed the forest size.");

        await base.VerifyAsync();
    }
}
