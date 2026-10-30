extern alias UserGroupsSample;
using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;
using ReadSnapshot = UserGroupsSample::Doka.EntityFrameworkCore.NestedSet.Samples.SampleReadSnapshot;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Compares independent per-path application chains with the sample's batched frontier.</summary>
/// <remarks>
/// Both paths share the actual structural candidate builder and pure person-selection contract. This reference
/// deliberately issues one bounded selection reader per active path; it is not a second role or query framework.
/// Physical row limits count actual reads, so repeated per-path status rows can exhaust a small budget earlier
/// than deduplicated frontier reads. Ordinary comparisons use sufficient physical budgets; exhaustion cases
/// compare the same initial reader or a single path. Logical frontier, visit, and result limits are unchanged.
/// </remarks>
internal static partial class UserGroupChainReference
{
    /// <summary>Evaluates all steps in one qualified snapshot while retaining every independent path.</summary>
    internal static Task<SupervisorChainResult> FollowAsync(
        UserGroupContext context,
        Guid tenantId,
        SupervisorChainRequest request,
        bool originalPath,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate(originalPath);

        return ReadSnapshot.RunAsync(
            context,
            token => FollowPathsAsync(context, tenantId, request, originalPath, token),
            cancellationToken);
    }

    /// <summary>Processes independent paths without search deduplication or batched UNION transport.</summary>
    private static async Task<SupervisorChainResult> FollowPathsAsync(
        UserGroupContext context,
        Guid tenantId,
        SupervisorChainRequest request,
        bool originalPath,
        CancellationToken cancellationToken
    )
    {
        var budget = new ReferenceBudget(request);
        var first = await ReadAsync(
            context,
            tenantId,
            Search(request, request.Steps[0], null, originalPath),
            request.Steps[0].Selection,
            budget,
            cancellationToken);

        if (first is null)
        {
            return Limited([], null);
        }

        if (first.Status != SupervisorBatchStatus.Completed)
        {
            return new SupervisorChainResult(first.Status, [], true);
        }

        var completed = new List<SupervisorChainBranch>();
        var active = new List<ReferencePath>();

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var origin in first.Origins)
        {
            if (!Append(
                    origin,
                    null,
                    1,
                    request,
                    request.Steps[0].Selection,
                    budget,
                    active,
                    completed))
            {
                return Limited(completed, null);
            }
        }

        // WHY: Evaluate requested hops in order and stop when no reference path remains.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 1; index < request.Steps.Count && active.Count != 0; index++)
        {
            var step = request.Steps[index];
            var next = new List<ReferencePath>();

            foreach (var path in active)
            {
                var batch = await ReadAsync(
                    context,
                    tenantId,
                    Search(request, step, path, originalPath),
                    step.Selection,
                    budget,
                    cancellationToken);

                if (batch is null)
                {
                    return Limited(completed, path);
                }

                if (batch.Status != SupervisorBatchStatus.Completed)
                {
                    if (!Finish(path, SupervisorChainStatus.NoSupervisor, budget, completed))
                    {
                        return Limited(completed, path);
                    }

                    continue;
                }

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var origin in batch.Origins)
                {
                    if (!Append(
                            origin,
                            path,
                            index + 1,
                            request,
                            step.Selection,
                            budget,
                            next,
                            completed))
                    {
                        return Limited(completed, path);
                    }
                }
            }

            active = next;
        }

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var path in active)
        {
            if (!Finish(path, SupervisorChainStatus.ReachedRequestedHop, budget, completed))
            {
                return Limited(completed, path);
            }
        }

        return new(SupervisorBatchStatus.Completed, completed, true);
    }

    /// <summary>Binds current identity, continuation cursor, and explicitly retained initial routing owners.</summary>
    private static SupervisorRequest Search(
        SupervisorChainRequest chain,
        SupervisorStep step,
        ReferencePath? path,
        bool originalPath
    )
    {
        var initialRouting = path is not null && chain.RoutingOrigin == RoutingOrigin.InitialSubject;
        var current = path?.Hops[^1].Evidence;

        return new SupervisorRequest
        {
            SourceUserId = current?.CandidateUserId ?? chain.SourceUserId,
            StartGroupId = current is null
                ? chain.StartGroupId
                : originalPath
                    ? current.FoundGroupId
                    : null,
            SearchStart = step.SearchStart,
            Roles = step.Roles,
            Groups = step.Groups,
            Matches = step.Matches,
            OnDay = step.OnDay,
            RoutingUserId =
                initialRouting && step.Matches.Any(match => match.SourceOwner != MetadataOwner.SourceGroup)
                    ? chain.SourceUserId
                    : null,
            RoutingGroupId = initialRouting && step.Matches.Any(match => match.SourceOwner != MetadataOwner.SourceUser)
                ? path!.InitialGroup
                : null,
        };
    }

    /// <summary>Checks every logical append before allocating a new complete application path.</summary>
    private static bool Append(
        SupervisorResolution resolution,
        ReferencePath? previous,
        int number,
        SupervisorChainRequest request,
        SupervisorSelection selection,
        ReferenceBudget budget,
        List<ReferencePath> next,
        List<SupervisorChainBranch> completed
    )
    {
        var initialGroup = previous?.InitialGroup ?? resolution.StartGroupId!.Value;

        if (resolution.Status != SupervisorResolutionStatus.Found)
        {
            if (!budget.Append((previous?.Hops.Length ?? 0) + 1))
            {
                return false;
            }

            completed.Add(
                new SupervisorChainBranch(
                    resolution.Status == SupervisorResolutionStatus.Ambiguous
                        ? SupervisorChainStatus.Ambiguous
                        : SupervisorChainStatus.NoSupervisor,
                    initialGroup,
                    previous?.Hops ?? []));

            return true;
        }

        var occurrences = selection.Kind == SupervisorSelectionKind.All
            ? resolution.Evidence
            : resolution.Continuation is { } continuation
                ? [continuation]
                : [];

        foreach (var evidence in occurrences)
        {
            if (evidence.CandidateUserId == request.SourceUserId
                || previous?.Hops.Any(hop => hop.Evidence.CandidateUserId == evidence.CandidateUserId) == true)
            {
                if (!budget.Append((previous?.Hops.Length ?? 0) + 1))
                {
                    return false;
                }

                completed.Add(
                    new SupervisorChainBranch(SupervisorChainStatus.CycleDetected, initialGroup, previous?.Hops ?? []));

                continue;
            }

            if (!budget.Visit()
                || !budget.Append(1)
                || next.Count == request.MaxFrontierStates)
            {
                return false;
            }

            // WHY: The baseline intentionally retains ordinary per-path arrays rather than sharing the
            // sample's prefix nodes. Both implementations admit the allocation through the same logical limits.
            var hops = new SupervisorHop[number];
            previous?.Hops.CopyTo(hops, 0);
            var tree = previous?.InitialTree ?? evidence.StartTreeId;
            hops[number - 1] = new(number, request.SourceUserId, initialGroup, tree, evidence);
            next.Add(new ReferencePath(initialGroup, tree, hops));
        }

        return true;
    }

    /// <summary>Accounts for retained terminal paths before exposing a successful or missing outcome.</summary>
    private static bool Finish(
        ReferencePath path,
        SupervisorChainStatus status,
        ReferenceBudget budget,
        List<SupervisorChainBranch> completed
    )
    {
        if (!budget.Append(path.Hops.Length + 1))
        {
            return false;
        }

        completed.Add(new SupervisorChainBranch(status, path.InitialGroup, path.Hops));

        return true;
    }

    /// <summary>Reports overflow without copying the already exhausted path into its fixed diagnostic row.</summary>
    private static SupervisorChainResult Limited(
        List<SupervisorChainBranch> completed,
        ReferencePath? path
    )
    {
        completed.Add(new SupervisorChainBranch(SupervisorChainStatus.LimitExceeded, path?.InitialGroup ?? 0, []));

        return new SupervisorChainResult(SupervisorBatchStatus.Completed, completed, false);
    }

    /// <summary>Retains initial provenance and exact cursor evidence for one independent application path.</summary>
    /// <param name="InitialGroup">The original authorized membership.</param>
    /// <param name="InitialTree">The original membership's stable tree.</param>
    /// <param name="Hops">The successful person hops in forward order.</param>
    private sealed record ReferencePath(
        int InitialGroup,
        Guid InitialTree,
        SupervisorHop[] Hops
    );

    /// <summary>Separates physical reader rows from logical path and visited-state budgets.</summary>
    private sealed class ReferenceBudget(SupervisorChainRequest request)
    {
        private int _reads;
        private int _visits;
        private int _results;

        /// <summary>Admits a physical row before the reference buffers it.</summary>
        internal bool Read() => _reads < request.MaxResultRows && ++_reads > 0;

        /// <summary>Admits a retained person-path node before a path array is allocated.</summary>
        internal bool Visit() => _visits < request.MaxVisitedStates && ++_visits > 0;

        /// <summary>Admits an overflow-safe logical append before copying result evidence.</summary>
        internal bool Append(
            int count
        )
        {
            if (count < 1
                || count > request.MaxResultRows - _results)
            {
                return false;
            }

            _results += count;

            return true;
        }
    }
}
