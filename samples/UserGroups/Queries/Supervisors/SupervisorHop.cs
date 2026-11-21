namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Records one successful hop with original and current membership provenance.</summary>
/// <param name="Number">The one-based hop.</param>
/// <param name="InitialUserId">The retained initial source.</param>
/// <param name="InitialGroupId">The retained initial membership group.</param>
/// <param name="InitialTreeId">The retained initial tree identity.</param>
/// <param name="Evidence">The exact current source, candidate membership, and tree evidence.</param>
internal sealed record SupervisorHop(
    int Number,
    int InitialUserId,
    int InitialGroupId,
    Guid InitialTreeId,
    SupervisorCandidate Evidence
);
