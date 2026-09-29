namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures public root, child and sibling insertion with identical starting catalogs.</summary>
public class InsertBenchmarks : DatabaseBenchmark
{
    private int? _parent;
    private long _position;
    private bool _inserted;
    private int _childCount;

    /// <summary>Appends an independently identified tree to the existing forest.</summary>
    [Benchmark]
    public async Task Root()
    {
        _parent = null;
        _position = 0;
        await Fixture.Hierarchy.InsertRootAsync(NewNode(), new Guid(-1, 0, 0, new byte[8]), CancellationToken.None);
        _inserted = true;
    }

    /// <summary>Inserts before every existing child.</summary>
    [Benchmark]
    public async Task FirstChild()
    {
        _parent = 1;
        _position = 0;
        await Fixture.Hierarchy.InsertAsFirstChildAsync(NewNode(), 1, CancellationToken.None);
        _inserted = true;
    }

    /// <summary>Appends after every existing child.</summary>
    [Benchmark]
    public async Task LastChild()
    {
        _parent = 1;
        _position = _childCount;
        await Fixture.Hierarchy.InsertAsLastChildAsync(NewNode(), 1, CancellationToken.None);
        _inserted = true;
    }

    /// <summary>Inserts a sibling before the first child.</summary>
    [Benchmark]
    public async Task Before()
    {
        _parent = 1;
        _position = 0;
        await Fixture.Hierarchy.InsertBeforeAsync(NewNode(), 2, CancellationToken.None);
        _inserted = true;
    }

    /// <summary>Inserts a sibling after the complete first-child subtree.</summary>
    [Benchmark]
    public async Task After()
    {
        _parent = 1;
        _position = 1;
        await Fixture.Hierarchy.InsertAfterAsync(NewNode(), 2, CancellationToken.None);
        _inserted = true;
    }

    /// <summary>Creates application input as part of the measured insertion call.</summary>
    /// <returns>A detached node with its application payload.</returns>
    private BenchmarkNode NewNode() => new()
    {
        Id = Nodes + 1,
        Name = "Inserted",
        Payload = new string('i', 1024),
    };

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _inserted = false;
        await base.PrepareAsync();
        _childCount = Fixture.Forest.Parents.Count(parent => parent == 1);
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        var node = await Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == Nodes + 1, CancellationToken.None);

        BenchmarkFixture.Require(
            _inserted && node.ParentId == _parent && node.Position == _position,
            "Insert placement did not occur.");

        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(CancellationToken.None) == Nodes + 1,
            "Insert count is incorrect.");

        await base.VerifyAsync();
    }
}
