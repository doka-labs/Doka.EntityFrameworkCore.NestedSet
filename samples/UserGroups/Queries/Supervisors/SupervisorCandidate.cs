namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Contains scalar evidence for one qualifying source-membership and candidate-membership pair.</summary>
internal sealed record SupervisorCandidate
{
    /// <summary>Gets the authorized tenant.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the person whose supervisor is being searched.</summary>
    public int SourceUserId { get; init; }

    /// <summary>Gets the exact source membership group.</summary>
    public int StartGroupId { get; init; }

    /// <summary>Gets the stable tree of this source path.</summary>
    public Guid StartTreeId { get; init; }

    /// <summary>Gets the qualifying candidate membership group.</summary>
    public int FoundGroupId { get; init; }

    /// <summary>Gets the candidate person identity.</summary>
    public int CandidateUserId { get; init; }

    /// <summary>Gets the actual stored edge distance, including hidden structural levels.</summary>
    public int Distance { get; init; }

    /// <summary>Gets the candidate membership's required business priority; smaller wins.</summary>
    public int LeadershipPriority { get; init; }
}
