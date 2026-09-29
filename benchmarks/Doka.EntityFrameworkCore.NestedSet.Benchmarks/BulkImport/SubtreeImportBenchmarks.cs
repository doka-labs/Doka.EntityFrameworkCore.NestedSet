namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures import of a new branch into an existing tree without timing input construction.</summary>
public class SubtreeImportBenchmarks : DatabaseBenchmark
{
    private NestedSetBranch<BenchmarkNode>? _branch;
    private bool _imported;

    /// <summary>Imports a branch one tenth the size of the existing forest beneath its first root.</summary>
    [Benchmark]
    public async Task ImportSubtree()
    {
        await Fixture.Hierarchy.InsertSubtreeAsync(
            _branch ?? throw new InvalidOperationException("Prepare the branch first."),
            1,
            CancellationToken.None);

        _imported = true;
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _imported = false;
        await base.PrepareAsync();
        var branch = BenchmarkForest.Build(new BenchmarkScenario(Math.Max(10, Nodes / 10), Shape, 1, 0), Nodes);
        _branch = branch.Imports[0].Root;
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        BenchmarkFixture.Require(
            _imported
            && await Fixture.Context.Nodes.CountAsync(CancellationToken.None) == Nodes + Math.Max(10, Nodes / 10),
            "Subtree import count is incorrect.");

        var root = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(node => node.Id == Nodes + 1, CancellationToken.None);

        BenchmarkFixture.Require(
            root.ParentId == 1 && root.TreeId == Fixture.Forest.Imports[0].TreeId,
            "Imported branch escaped its destination tree.");

        await base.VerifyAsync();
    }
}
