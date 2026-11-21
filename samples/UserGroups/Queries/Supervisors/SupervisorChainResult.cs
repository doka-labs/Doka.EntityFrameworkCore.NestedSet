namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Preserves request-level absence and individual chain outcomes.</summary>
/// <param name="Status">The initial visible-source and own-membership status.</param>
/// <param name="Branches">Every evaluated path or an explicit incomplete limit result.</param>
/// <param name="Complete">Whether every requested branch was evaluated within all budgets.</param>
internal sealed record SupervisorChainResult(
    SupervisorBatchStatus Status,
    IReadOnlyList<SupervisorChainBranch> Branches,
    bool Complete
);
