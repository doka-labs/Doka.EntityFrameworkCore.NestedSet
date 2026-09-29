namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures import of an already constructed application forest plan into an empty owned catalog.</summary>
public class ForestImportBenchmarks : DatabaseBenchmark
{
    private bool _imported;

    /// <inheritdoc />
    protected override bool Seed => false;

    /// <summary>Persists the complete detached forest through the public atomic bulk API.</summary>
    [Benchmark]
    public async Task ImportForest()
    {
        await Fixture.Hierarchy.InsertForestAsync(Fixture.Forest.Imports, CancellationToken.None);
        _imported = true;
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _imported = false;
        await base.PrepareAsync();
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        BenchmarkFixture.Require(
            _imported && await Fixture.Context.Nodes.CountAsync(CancellationToken.None) == Nodes,
            "Forest import did not persist every node.");

        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(node => node.ParentId == null, CancellationToken.None) == Trees,
            "Forest import changed root ownership.");

        await base.VerifyAsync();
    }
}
