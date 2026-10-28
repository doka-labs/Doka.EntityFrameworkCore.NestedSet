namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{
    /// <summary>Checks logical fan-out before appending any person, visited node, or next-frontier copy.</summary>
    /// <param name="resolution">One fully evaluated origin, including its person-selection outcome.</param>
    /// <param name="previous">The shared immutable successful prefix, or null on the first hop.</param>
    /// <param name="initialUser">The initial subject used for per-path cycle detection.</param>
    /// <param name="currentUser">The current subject, which candidate composition must exclude.</param>
    /// <param name="initialGroup">The exact original membership retained in every result.</param>
    /// <param name="initialTree">The original tree retained after the first successful hop.</param>
    /// <param name="hop">The one-based current step.</param>
    /// <param name="selection">The person rule determining all occurrences or one canonical cursor.</param>
    /// <param name="budget">The shared physical and logical bounds for the complete operation.</param>
    /// <param name="next">The bounded next logical frontier.</param>
    /// <param name="completed">The bounded terminal paths, including explicit failure outcomes.</param>
    /// <returns>Whether the requested branch fit all budgets before allocating its retained results.</returns>
    internal static bool AdvanceBranch(
        SupervisorResolution resolution,
        ChainPath? previous,
        int initialUser,
        int currentUser,
        int initialGroup,
        Guid initialTree,
        int hop,
        SupervisorSelection selection,
        ChainBudget budget,
        List<ChainPath> next,
        List<SupervisorChainBranch> completed
    )
    {
        if (resolution.Status != SupervisorResolutionStatus.Found)
        {
            if (!budget.Result((previous?.Hop.Number ?? 0) + 1))
            {
                return false;
            }

            completed.Add(
                new SupervisorChainBranch(
                    resolution.Status == SupervisorResolutionStatus.Ambiguous
                        ? SupervisorChainStatus.Ambiguous
                        : SupervisorChainStatus.NoSupervisor,
                    initialGroup,
                    previous?.Materialize() ?? []));

            return true;
        }

        var allOccurrences = selection.Kind == SupervisorSelectionKind.All;
        var continuation = resolution.Continuation;
        // WHY: Unique selection already supplies its canonical occurrence. A one-element collection here
        // would allocate an extra array for every logical path without retaining any additional evidence.
        var count = allOccurrences
            ? resolution.Evidence.Count
            : continuation is null
                ? 0
                : 1;

        // WHY: Visit either the evidence bag or its canonical occurrence without allocating a one-element
        // collection.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < count; index++)
        {
            var evidence = allOccurrences ? resolution.Evidence[index] : continuation!;
            if (evidence.CandidateUserId == initialUser
                || previous?.Contains(evidence.CandidateUserId) == true)
            {
                if (!budget.Result((previous?.Hop.Number ?? 0) + 1))
                {
                    return false;
                }

                completed.Add(new SupervisorChainBranch(SupervisorChainStatus.CycleDetected, initialGroup, previous?.Materialize() ?? []));

                continue;
            }

            if (evidence.CandidateUserId == currentUser)
            {
                throw new InvalidOperationException("Candidate queries must exclude the current source person.");
            }

            if (!budget.Visit()
                || !budget.Result()
                || next.Count == budget.Request.MaxFrontierStates)
            {
                return false;
            }

            var tree = previous is null ? evidence.StartTreeId : initialTree;
            var item = new SupervisorHop(hop, initialUser, initialGroup, tree, evidence);
            next.Add(new ChainPath(item, previous, initialGroup, tree));
        }

        return true;
    }

    /// <summary>Appends one terminal status only after the result budget has admitted it.</summary>
    private static bool Complete(
        ChainPath path,
        SupervisorChainStatus status,
        ChainBudget budget,
        List<SupervisorChainBranch> completed
    )
    {
        if (!budget.Result(path.Hop.Number + 1))
        {
            return false;
        }

        completed.Add(new SupervisorChainBranch(status, path.InitialGroupId, path.Materialize()));

        return true;
    }

    /// <summary>Marks an incomplete response explicitly rather than presenting an arbitrary truncated winner.</summary>
    private static SupervisorChainResult LimitResult(
        List<SupervisorChainBranch> completed,
        ChainPath? path
    )
    {
        // WHY: The fixed overflow diagnostic cannot copy an already exhausted path budget.
        completed.Add(new SupervisorChainBranch(SupervisorChainStatus.LimitExceeded, path?.InitialGroupId ?? 0, []));

        return new SupervisorChainResult(SupervisorBatchStatus.Completed, completed, false);
    }
}
