namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SaveValidationLifetimeTests
{
    /// <summary>Distinguishes an injected payload failure from guard or context initialization failures.</summary>
    private sealed class SaveFailureException : Exception;

    /// <summary>Captures both sides of one guard lifetime without retaining the context itself.</summary>
    private sealed record BoundaryObservation(
        Action? During,
        Action? After,
        bool ActiveDuring,
        int Calls,
        Exception? Failure
    );

    /// <summary>Captures lease identity and the untouched validation slot of the reused context.</summary>
    private sealed record PoolObservation(
        bool SameContext,
        bool SameState,
        BoundaryObservation First,
        Action? NextLeaseValidation,
        bool NextLeaseActive,
        CancellationToken CancellationToken
    );

    /// <summary>Captures guard identity, intentional flag tampering, and the result of one sync save.</summary>
    private sealed record SynchronousObservation(
        Action? During,
        Action? After,
        bool ActiveDuring,
        bool DetectionDuring,
        bool DetectionAfter,
        int Calls,
        Exception? Failure,
        CancellationToken CancellationToken
    );

    /// <summary>Captures a synchronous boundary's state before and after a real EF pool lease transition.</summary>
    private sealed record SynchronousPoolObservation(
        bool SameContext,
        bool SameState,
        SynchronousObservation First,
        Action? NextLeaseValidation,
        bool NextLeaseActive,
        bool NextLeaseDetection
    );
}
