namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures subtree deletion and deletion with adjacency-preserving child promotion.</summary>
public class DeleteBenchmarks : DatabaseBenchmark
{
    private int _expected;
    private bool _deleted;
    private int _subtreeSize;

    /// <summary>Deletes one branch root and promotes its immediate children.</summary>
    [Benchmark]
    public async Task PromoteChildren()
    {
        _expected = Nodes - 1;
        await Fixture.Hierarchy.DeleteAsync(2, CancellationToken.None);
        _deleted = true;
    }

    /// <summary>Deletes node two and its complete shape-dependent subtree.</summary>
    [Benchmark]
    public async Task Subtree()
    {
        _expected = Nodes - _subtreeSize;
        await Fixture.Hierarchy.DeleteSubtreeAsync(2, CancellationToken.None);
        _deleted = true;
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _deleted = false;
        await base.PrepareAsync();

        // WHY: Derive expected deletion work outside measurement, independently of persisted interval calculations.
        var descendants = new HashSet<int> { 2 };

        for (var index = 2; index < Fixture.Forest.Parents.Length; index++)
        {
            if (Fixture.Forest.Parents[index] is { } parent
                && descendants.Contains(parent))
            {
                descendants.Add(index + 1);
            }
        }

        _subtreeSize = descendants.Count;
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        BenchmarkFixture.Require(
            _deleted && !await Fixture.Context.Nodes.AnyAsync(node => node.Id == 2, CancellationToken.None),
            "Deletion did not occur.");

        BenchmarkFixture.Require(
            await Fixture.Context.Nodes.CountAsync(CancellationToken.None) == _expected,
            "Deletion removed the wrong number of nodes.");

        await base.VerifyAsync();
    }
}
