namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Contains the complete request status and every visible evaluated origin.</summary>
/// <param name="Status">The source/membership request status.</param>
/// <param name="Origins">Per-origin outcomes preserving zero matches.</param>
/// <param name="Complete">Whether all requested rows were read within the explicit safety budget.</param>
internal sealed record SupervisorResolutionBatch(
    SupervisorBatchStatus Status,
    IReadOnlyList<SupervisorResolution> Origins,
    bool Complete = true
);
