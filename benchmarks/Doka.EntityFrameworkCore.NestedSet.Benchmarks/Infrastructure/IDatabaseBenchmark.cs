namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies the existing resource and diagnostic lifecycle without prescribing a consumer model.</summary>
/// <remarks>
/// WHY: Real sample benchmarks own their actual DbContexts. Requiring the generic hierarchy fixture would
/// allocate unused forests and benchmark a facade around the wrong model instead of the consumer queries.
/// </remarks>
internal interface IDatabaseBenchmark
{
    /// <summary>Gets operation-only observations for the existing untimed diagnostic pass.</summary>
    CommandObservation Observation { get; }

    /// <summary>Acquires the child-owned context after launcher resource preflight.</summary>
    void Initialize();

    /// <summary>Prepares seeded inputs outside measured work.</summary>
    /// <param name="cancellationToken">Cancellation for database reset and fixture preparation.</param>
    Task PrepareAsync(
        CancellationToken cancellationToken
    );

    /// <summary>Verifies the measured operation after timing and allocation observation stop.</summary>
    /// <param name="cancellationToken">Cancellation for the verification reads.</param>
    Task VerifyAsync(
        CancellationToken cancellationToken
    );

    /// <summary>Releases only resources owned by the isolated benchmark child.</summary>
    /// <remarks>Cleanup is deliberately independent of the operation's cancellation.</remarks>
    Task CleanupAsync();
}
