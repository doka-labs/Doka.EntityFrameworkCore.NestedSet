namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Observes dictionary equality and hashing through typed and deliberately boxed EF comparers.</summary>
public class KeyComparerMeasurementBenchmarks
{
    private Dictionary<int, int> _dictionary = null!;

    /// <summary>Gets or sets whether a nullable comparer exercises the required object fallback.</summary>
    [Params(false, true)]
    public bool BoxedFallback { get; set; }

    /// <summary>Creates and checks the populated dictionary outside measured invocations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ValueComparer comparer = BoxedFallback
            ? new ValueComparer<int?>((first, second) => first == second, value => value.GetHashCode())
            : new ValueComparer<int>((first, second) => first == second, value => value.GetHashCode());

        _dictionary = new Dictionary<int, int>(new NestedSetKeyComparer<int>(comparer)) { [42] = 1 };
        VerifyWorkload();
    }

    /// <summary>Looks up one existing identity through the actual production key comparer.</summary>
    /// <returns>Whether the populated dictionary still contains the exact identity.</returns>
    [Benchmark]
    public bool ContainsKey() => _dictionary.ContainsKey(42);

    /// <summary>Retains present/missing-key semantics without assigning a numeric allocation verdict.</summary>
    [GlobalCleanup]
    public void VerifyWorkload()
    {
        if (!_dictionary.TryGetValue(42, out var value) || value != 1 || _dictionary.ContainsKey(43))
        {
            throw new InvalidOperationException("The key-comparer dictionary workload changed.");
        }
    }
}
