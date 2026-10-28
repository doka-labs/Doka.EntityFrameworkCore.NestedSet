namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Retains bounded result, command, allocation, and plan evidence from an actual consumer operation.</summary>
/// <param name="Profile">The application operation and selected shared topology.</param>
/// <param name="Actual">The canonical scalar output returned by the real sample builder.</param>
/// <param name="Expected">The independent adjacency/domain oracle.</param>
/// <param name="Commands">The operation-only asynchronous database commands.</param>
/// <param name="ExpectedCommands">The reader budget for the requested consumer operation.</param>
/// <param name="MaximumParameters">
/// The largest native binding count among operation readers before plan capture.
/// </param>
/// <param name="ConstructionCommands">Commands observed while building the complete warmed expression.</param>
/// <param name="Materialized">Domain entities reported by EF's materialization interceptor.</param>
/// <param name="Tracked">Entries retained by the actual sample context.</param>
/// <param name="BuildBytes">Own warmed expression allocation, excluding setup and request arrays.</param>
/// <param name="BuildBudget">The contract's fixed allowance for this criterion count.</param>
/// <param name="Elapsed">Observed duration, never a machine-dependent CPU gate.</param>
/// <param name="Plan">The provider-native plan of an actual measured command.</param>
internal sealed record CompositionConsumerCapacityObservation(
    string Profile,
    string[] Actual,
    string[] Expected,
    int Commands,
    int ExpectedCommands,
    int MaximumParameters,
    int ConstructionCommands,
    int Materialized,
    int Tracked,
    long BuildBytes,
    long BuildBudget,
    TimeSpan Elapsed,
    string Plan
);
