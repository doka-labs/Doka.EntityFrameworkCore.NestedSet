namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

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
