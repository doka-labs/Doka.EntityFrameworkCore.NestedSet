namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Runs explicitly different chain semantics within one qualified read snapshot.</summary>
internal static partial class SupervisorQueries
{
    /// <summary>Advances a bounded frontier sequentially; one context never runs parallel readers.</summary>
    private static async Task<SupervisorChainResult> FollowAsync(
        UserGroupContext context,
        Guid tenantId,
        SupervisorChainRequest request,
        bool originalPath,
        CancellationToken cancellationToken
    )
    {
        var state = new ChainBudget(request);
        var first = ToRequest(request.SourceUserId, request.StartGroupId, request.Steps[0], null, null);
        var initialRows = await ReadFrontierAsync(
            context,
            tenantId,
            [(first, request.Steps[0].Selection)],
            state,
            cancellationToken);

        if (initialRows is null)
        {
            return LimitResult([], null);
        }

        var initial = initialRows[0];
        if (initial.Status != SupervisorBatchStatus.Completed)
        {
            return new SupervisorChainResult(initial.Status, [], true);
        }

        var frontier = new List<ChainPath>();
        var completed = new List<SupervisorChainBranch>();
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var origin in initial.Origins)
        {
            if (!AdvanceBranch(
                    origin,
                    null,
                    request.SourceUserId,
                    request.SourceUserId,
                    origin.StartGroupId!.Value,
                    Guid.Empty,
                    1,
                    request.Steps[0].Selection,
                    state,
                    frontier,
                    completed))
            {
                return LimitResult(completed, null);
            }
        }

        // WHY: Evaluate hops in request order and stop when the frontier becomes empty.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var stepIndex = 1; stepIndex < request.Steps.Count && frontier.Count != 0; stepIndex++)
        {
            var step = request.Steps[stepIndex];
            var searches = new List<(SupervisorRequest Request, SupervisorSelection Selection)>();
            var indexes = new Dictionary<ChainSearchKey, int>();
            var physical = new int[frontier.Count];
            var keepRoutingUser = request.RoutingOrigin == RoutingOrigin.InitialSubject
                && step.Matches.Any(match => match.SourceOwner != MetadataOwner.SourceGroup);

            var keepRoutingGroup = request.RoutingOrigin == RoutingOrigin.InitialSubject
                && step.Matches.Any(match => match.SourceOwner != MetadataOwner.SourceUser);

            // WHY: Keep frontier positions aligned with the physical-query ordinal buffer.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < frontier.Count; index++)
            {
                var path = frontier[index];
                var routingUser = keepRoutingUser ? request.SourceUserId : (int?)null;
                var routingGroup = keepRoutingGroup ? path.InitialGroupId : (int?)null;
                var cursor = originalPath ? path.Hop.Evidence.FoundGroupId : (int?)null;
                var key = new ChainSearchKey(path.Hop.Evidence.CandidateUserId, cursor, routingUser, routingGroup);

                if (!indexes.TryGetValue(key, out var ordinal))
                {
                    ordinal = searches.Count;
                    searches.Add(
                        (ToRequest(key.User, key.Group, step, key.RoutingUser, key.RoutingGroup), step.Selection));
                    indexes.Add(key, ordinal);
                }

                physical[index] = ordinal;
            }

            var batches = await ReadFrontierAsync(context, tenantId, searches, state, cancellationToken);
            if (batches is null)
            {
                return LimitResult(completed, frontier[0]);
            }

            var next = new List<ChainPath>();

            // WHY: Select each frontier result through its matching stored physical-query ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < frontier.Count; index++)
            {
                var path = frontier[index];
                var batch = batches[physical[index]];
                if (batch.Status != SupervisorBatchStatus.Completed)
                {
                    if (!Complete(path, SupervisorChainStatus.NoSupervisor, state, completed))
                    {
                        return LimitResult(completed, path);
                    }

                    continue;
                }

                foreach (var origin in batch.Origins)
                {
                    if (!AdvanceBranch(
                            origin,
                            path,
                            request.SourceUserId,
                            path.Hop.Evidence.CandidateUserId,
                            path.InitialGroupId,
                            path.InitialTreeId,
                            stepIndex + 1,
                            step.Selection,
                            state,
                            next,
                            completed))
                    {
                        return LimitResult(completed, path);
                    }
                }
            }

            frontier = next;
        }

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var path in frontier)
        {
            if (!Complete(path, SupervisorChainStatus.ReachedRequestedHop, state, completed))
            {
                return LimitResult(completed, path);
            }
        }

        return new(SupervisorBatchStatus.Completed, completed, true);
    }

    /// <summary>Builds one exact current-subject search, optionally retaining the initial routing owner.</summary>
    private static SupervisorRequest ToRequest(
        int source,
        int? group,
        SupervisorStep step,
        int? routingUser,
        int? routingGroup
    ) => new()
    {
        SourceUserId = source,
        StartGroupId = group,
        SearchStart = step.SearchStart,
        Roles = step.Roles,
        Groups = step.Groups,
        Matches = step.Matches,
        OnDay = step.OnDay,
        RoutingUserId = routingUser,
        RoutingGroupId = routingGroup,
    };
}
