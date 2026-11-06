namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures configured sibling insertion with no hierarchy entries preloaded into the tracker.</summary>
public class OrderingBenchmarks : DatabaseBenchmark
{
    private int _expected;
    private int _key;

    /// <inheritdoc />
    protected override bool Ordered => true;

    /// <summary>Inserts a node whose configured name places it before the existing first child.</summary>
    [Benchmark]
    public async Task SortedInsert()
    {
        _key = Nodes + 1;
        _expected = Nodes + 1;
        await Fixture.Hierarchy.InsertChildAsync(
            new BenchmarkNode
            {
                Id = _key,
                Name = "000-first",
            },
            1,
            CancellationToken.None);
    }

    /// <inheritdoc />
    public override async Task PrepareAsync(
        CancellationToken cancellationToken
    )
    {
        _key = 0;
        await base.PrepareAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task VerifyAsync(
        CancellationToken cancellationToken
    )
    {
        var node = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == _key, cancellationToken);

        BenchmarkFixture.Require(node is { Name: "000-first", Position: 0 }, "Configured ordering did not occur.");
        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(cancellationToken) == _expected,
            "Ordering changed the node count.");

        await base.VerifyAsync(cancellationToken);
    }
}
