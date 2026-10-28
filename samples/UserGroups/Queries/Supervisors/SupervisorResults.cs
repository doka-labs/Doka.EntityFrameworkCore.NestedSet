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

/// <summary>Distinguishes an absent visible source from absent own memberships and evaluated paths.</summary>
internal enum SupervisorBatchStatus
{
    /// <summary>The authorized visible source user does not exist.</summary>
    SourceNotFound,

    /// <summary>The source exists but has no selected visible valid own memberships.</summary>
    NoVisibleMemberships,

    /// <summary>The selected visible origins were evaluated.</summary>
    Completed,
}

/// <summary>Reports person-selection outcomes without inventing a winner for equal candidates.</summary>
internal enum SupervisorResolutionStatus
{
    /// <summary>The authorized visible source user does not exist.</summary>
    SourceNotFound,

    /// <summary>No selected visible valid own origin exists.</summary>
    NoVisibleMemberships,

    /// <summary>The requested person selection succeeded.</summary>
    Found,

    /// <summary>The evaluated origin has no qualifying selected person.</summary>
    NotFound,

    /// <summary>The explicit person rule cannot establish one winner.</summary>
    Ambiguous,

    /// <summary>The input evidence is incomplete because its safety budget was exceeded.</summary>
    LimitExceeded,
}

/// <summary>Names the four distinct person-selection contracts.</summary>
internal enum SupervisorSelectionKind
{
    /// <summary>Retains every qualifying occurrence, with output-proportional memory.</summary>
    All,

    /// <summary>Requires exactly one distinct candidate person.</summary>
    RequireUnique,

    /// <summary>Requires one distinct person at the smallest membership leadership priority.</summary>
    HighestBusinessPriority,

    /// <summary>Selects a one-based person slot while retaining ambiguous occupied tie slots.</summary>
    NthPerson,
}

/// <summary>Specifies one person-selection alternative.</summary>
/// <param name="Kind">The explicit selection rule.</param>
/// <param name="Number">The one-based person slot; used only by NthPerson.</param>
internal sealed record SupervisorSelection(
    SupervisorSelectionKind Kind,
    int? Number = null
)
{
    /// <summary>Gets the rule retaining every qualifying occurrence.</summary>
    internal static SupervisorSelection All { get; } = new(SupervisorSelectionKind.All);

    /// <summary>Gets the rule requiring exactly one distinct person.</summary>
    internal static SupervisorSelection RequireUnique { get; } = new(SupervisorSelectionKind.RequireUnique);

    /// <summary>Gets the rule selecting the unique person at the smallest business priority.</summary>
    internal static SupervisorSelection HighestBusinessPriority { get; } =
        new(SupervisorSelectionKind.HighestBusinessPriority);

    /// <summary>Creates an explicit one-based person-slot rule.</summary>
    internal static SupervisorSelection NthPerson(
        int number
    ) => new(SupervisorSelectionKind.NthPerson, number);

    /// <summary>Rejects ignored rank arguments and nonpositive slots before I/O.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(Kind)
            || (Kind == SupervisorSelectionKind.NthPerson ? Number is null or <= 0 : Number is not null))
        {
            throw new ArgumentException("Person slots are positive and belong only to NthPerson.");
        }
    }
}

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

/// <summary>Contains the complete request status and every visible evaluated origin.</summary>
/// <param name="Status">The source/membership request status.</param>
/// <param name="Origins">Per-origin outcomes preserving zero matches.</param>
/// <param name="Complete">Whether all requested rows were read within the explicit safety budget.</param>
internal sealed record SupervisorResolutionBatch(
    SupervisorBatchStatus Status,
    IReadOnlyList<SupervisorResolution> Origins,
    bool Complete = true
);

/// <summary>Requires a unique person across all origins or selects an explicitly prioritized own origin.</summary>
internal abstract record AcrossOriginsSelection;

/// <summary>Requires the same unique person across the complete set of origins.</summary>
internal sealed record RequireUniquePerson : AcrossOriginsSelection;

/// <summary>
/// Uses the first present allowed origin, retaining its missing or ambiguous outcome without fallback.
/// </summary>
/// <param name="GroupIds">The application-defined ordered, duplicate-free allowed origins.</param>
internal sealed record FromPrioritizedOrigin(IReadOnlyList<int> GroupIds) : AcrossOriginsSelection;
