namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Retains bounded scalar evidence after an untimed seed and one measured relational read.</summary>
/// <param name="Profile">The topology, persisted size, anchor count, and selective payload shape.</param>
/// <param name="Rows">The scalar grouped rows returned by the database.</param>
/// <param name="Expected">The independently derived grouped contributions.</param>
/// <param name="Commands">The asynchronous commands executed by the query alone.</param>
/// <param name="Parameters">The actual native bindings of the measured query.</param>
/// <param name="ConstructionCommands">The commands observed while constructing the warmed query.</param>
/// <param name="Materialized">The hierarchy entities materialized by the scalar query.</param>
/// <param name="Tracked">The tracker entries retained after execution.</param>
/// <param name="BuildBytes">The warmed allocation per query construction, without executing it.</param>
/// <param name="Elapsed">The observed query duration, which is never a CPU acceptance threshold.</param>
/// <param name="Plan">The native plan for the command and its original parameter bindings.</param>
internal sealed record CompositionCapacityObservation(
    string Profile,
    CompositionCapacityTotal[] Rows,
    CompositionCapacityTotal[] Expected,
    int Commands,
    int Parameters,
    int ConstructionCommands,
    int Materialized,
    int Tracked,
    long BuildBytes,
    TimeSpan Elapsed,
    string Plan
);
