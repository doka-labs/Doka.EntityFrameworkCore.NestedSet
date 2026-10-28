namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies the existing resource and diagnostic lifecycle without prescribing a consumer model.</summary>
/// <remarks>
/// WHY: Real sample benchmarks own their actual DbContexts. Requiring the generic hierarchy fixture would
/// allocate unused forests and benchmark a facade around the wrong model instead of the consumer queries.
/// </remarks>
internal interface IDatabaseBenchmark
{
    /// <summary>Acquires the child-owned context after launcher resource preflight.</summary>
    void Initialize();

    /// <summary>Prepares seeded inputs outside measured work.</summary>
    Task PrepareAsync();

    /// <summary>Verifies the measured operation after timing and allocation observation stop.</summary>
    Task VerifyAsync();

    /// <summary>Releases only resources owned by the isolated benchmark child.</summary>
    Task CleanupAsync();

    /// <summary>Gets operation-only observations for the existing untimed diagnostic pass.</summary>
    CommandObservation Observation { get; }
}
