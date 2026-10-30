namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Chooses whether routing follows the initial subject or each newly selected subject.</summary>
internal enum RoutingOrigin
{
    /// <summary>Retains the initial user and exact origin membership as routing owners at every hop.</summary>
    InitialSubject,

    /// <summary>Uses each selected user and its currently expanded origin membership as routing owners.</summary>
    CurrentSubject,
}

/// <summary>Reports the termination of one independent provenance-preserving chain branch.</summary>
internal enum SupervisorChainStatus
{
    /// <summary>The finite requested sequence completed successfully.</summary>
    ReachedRequestedHop,

    /// <summary>The next evaluated step had no qualifying person.</summary>
    NoSupervisor,

    /// <summary>The next step could not select one person under its explicit rule.</summary>
    Ambiguous,

    /// <summary>The next selected person already appeared on this independent path.</summary>
    CycleDetected,

    /// <summary>A mandatory frontier, visited, or result budget stopped complete evaluation.</summary>
    LimitExceeded,
}

/// <summary>Contains one explicit search and person-selection rule applied at a single hop.</summary>
/// <param name="SearchStart">Whether the hop includes its own membership group.</param>
/// <param name="Roles">The person-role source.</param>
/// <param name="Groups">The group/rank selection.</param>
/// <param name="Matches">The exact scalar owner matches.</param>
/// <param name="OnDay">The inclusive validity date, if requested.</param>
/// <param name="Selection">The person-selection rule.</param>
internal sealed record SupervisorStep(
    SearchStart SearchStart,
    SupervisorRoles Roles,
    SupervisorGroups Groups,
    IReadOnlyList<MetadataMatch> Matches,
    DateOnly? OnDay,
    SupervisorSelection Selection
);

/// <summary>Requires finite steps and explicit bounds before any chain query can execute.</summary>
internal sealed record SupervisorChainRequest
{
    /// <summary>Gets the initial user identity.</summary>
    internal required int SourceUserId { get; init; }

    /// <summary>Gets the optional exact initial own membership.</summary>
    internal int? StartGroupId { get; init; }

    /// <summary>Gets every explicit hop; its count is the requested hop count.</summary>
    internal required IReadOnlyList<SupervisorStep> Steps { get; init; }

    /// <summary>Gets the explicitly retained routing subject.</summary>
    internal required RoutingOrigin RoutingOrigin { get; init; }

    /// <summary>Gets the maximum number of active logical paths before any next-frontier allocation.</summary>
    internal required int MaxFrontierStates { get; init; }

    /// <summary>Gets the maximum number of retained person-path nodes before allocation.</summary>
    internal required int MaxVisitedStates { get; init; }

    /// <summary>
    /// Gets the maximum physical status/evidence and logical result rows, from one through Int32.MaxValue minus one.
    /// </summary>
    internal required int MaxResultRows { get; init; }

    /// <summary>Validates every step before a transaction or reader is opened.</summary>
    internal void Validate(
        bool originalPath
    )
    {
        ArgumentNullException.ThrowIfNull(Steps);
        if (Steps.Count == 0
            || MaxFrontierStates < 1
            || MaxVisitedStates < 1
            || MaxResultRows < 1
            || MaxResultRows == int.MaxValue
            || !Enum.IsDefined(RoutingOrigin))
        {
            // WHY: One extra SQL row must fit Int32 Take to detect overflow before EF can buffer the reader.
            throw new ArgumentException(
                "Chains require finite nonempty steps, positive budgets, and MaxResultRows below Int32.MaxValue.");
        }

        // WHY: Validate hops in order while applying the distinct initial-hop and later-hop rules.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Steps.Count; index++)
        {
            var step = Steps[index] ?? throw new ArgumentException("Every hop needs an explicit rule.");
            ArgumentNullException.ThrowIfNull(step.Selection);
            step.Selection.Validate();
            SupervisorRequestValidation.Validate(
                new SupervisorRequest
                {
                    SourceUserId = SourceUserId,
                    SearchStart = step.SearchStart,
                    Roles = step.Roles,
                    Groups = step.Groups,
                    Matches = step.Matches,
                    OnDay = step.OnDay,
                });

            if (originalPath
                && index > 0
                && step.SearchStart != SearchStart.ParentsOnly)
            {
                throw new ArgumentException("OriginalPath requires ParentsOnly after the first hop.");
            }
        }
    }
}

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

/// <summary>Contains one independent chain path and its explicit completion outcome.</summary>
/// <param name="Status">The path termination reason.</param>
/// <param name="InitialGroupId">The original own membership group.</param>
/// <param name="Hops">Only successfully traversed person hops.</param>
internal sealed record SupervisorChainBranch(
    SupervisorChainStatus Status,
    int InitialGroupId,
    IReadOnlyList<SupervisorHop> Hops
);

/// <summary>Preserves request-level absence and individual chain outcomes.</summary>
/// <param name="Status">The initial visible-source and own-membership status.</param>
/// <param name="Branches">Every evaluated path or an explicit incomplete limit result.</param>
/// <param name="Complete">Whether every requested branch was evaluated within all budgets.</param>
internal sealed record SupervisorChainResult(
    SupervisorBatchStatus Status,
    IReadOnlyList<SupervisorChainBranch> Branches,
    bool Complete
);
