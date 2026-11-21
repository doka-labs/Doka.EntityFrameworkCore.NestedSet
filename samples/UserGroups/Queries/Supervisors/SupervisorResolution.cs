namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Retains a selected person's evidence and the exact canonical continuation occurrence.</summary>
/// <param name="Status">The explicit outcome.</param>
/// <param name="StartGroupId">The evaluated origin, or null for a cross-origin outcome.</param>
/// <param name="Evidence">All retained evidence; an ambiguous result never pretends to select a person.</param>
/// <param name="Continuation">The canonical occurrence for an explicitly unique person.</param>
internal sealed record SupervisorResolution(
    SupervisorResolutionStatus Status,
    int? StartGroupId,
    IReadOnlyList<SupervisorCandidate> Evidence,
    SupervisorCandidate? Continuation
);
