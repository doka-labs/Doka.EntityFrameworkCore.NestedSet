namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures pure core interval calculations separately from database and EF initialization.</summary>
public class BoundsBenchmarks
{
    private NestedSetBounds[] _bounds = [];
    private NestedSetBounds _root;

    /// <summary>Gets or sets the number of interval calculations per invocation.</summary>
    [ParamsSource(nameof(NodeCounts))]
    public int Nodes { get; set; }

    /// <summary>Gets the explicitly selected calculation sizes.</summary>
    public static IEnumerable<int> NodeCounts => BenchmarkRunOptions.Current.NodeCounts;

    /// <summary>Prepares immutable inputs outside measurement.</summary>
    [GlobalSetup]
    public void Prepare()
    {
        _root = new NestedSetBounds(1, ((long)Nodes * 2) + 2);
        _bounds = Enumerable
            .Range(0, Nodes)
            .Select(index => new NestedSetBounds((index * 2L) + 2, (index * 2L) + 3))
            .ToArray();
    }

    /// <summary>Returns consumed containment results so the JIT cannot discard the calculations.</summary>
    /// <returns>The number of contained leaf intervals.</returns>
    [Benchmark]
    public int CountContainedLeaves()
    {
        var count = 0;

        foreach (var bounds in _bounds)
        {
            if (_root.Contains(bounds)
                && bounds.IsLeaf)
            {
                count++;
            }
        }

        return count;
    }
}
