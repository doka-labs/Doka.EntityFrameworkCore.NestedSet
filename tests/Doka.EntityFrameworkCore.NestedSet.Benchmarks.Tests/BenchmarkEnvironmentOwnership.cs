namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Serializes tests that temporarily publish the launcher's inherited child-process environment.</summary>
/// <remarks>
/// WHY: The environment belongs to the entire process and must not overlap independent database tests.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BenchmarkEnvironmentOwnership
{
    /// <summary>Identifies the collection that exclusively owns inherited benchmark settings.</summary>
    public const string Name = "Benchmark environment ownership";
}
