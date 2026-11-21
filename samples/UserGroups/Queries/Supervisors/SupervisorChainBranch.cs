namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Contains one independent chain path and its explicit completion outcome.</summary>
/// <param name="Status">The path termination reason.</param>
/// <param name="InitialGroupId">The original own membership group.</param>
/// <param name="Hops">Only successfully traversed person hops.</param>
internal sealed record SupervisorChainBranch(
    SupervisorChainStatus Status,
    int InitialGroupId,
    IReadOnlyList<SupervisorHop> Hops
);
