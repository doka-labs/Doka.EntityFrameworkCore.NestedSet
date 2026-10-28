namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Contains pure, independently testable person and origin reductions used by the sample.</summary>
internal static class SupervisorSelectionRules
{
    /// <summary>
    /// Resolves one complete materialized candidate set; database queries reduce fan-in before this boundary.
    /// </summary>
    internal static SupervisorResolution Select(
        int origin,
        IReadOnlyList<SupervisorCandidate> candidates,
        SupervisorSelection selection
    ) => SelectCore(origin, candidates, selection, ownsEvidence: false);

    /// <summary>Transfers an exclusively owned evidence array into the selected result when every row wins.</summary>
    /// <param name="origin">The exact source membership group.</param>
    /// <param name="candidates">A fresh array whose slots the caller must not mutate after this transfer.</param>
    /// <param name="selection">The explicit person-selection contract.</param>
    /// <returns>A complete outcome retaining the transferred array or a snapshot of the winning subset.</returns>
    internal static SupervisorResolution SelectOwned(
        int origin,
        SupervisorCandidate[] candidates,
        SupervisorSelection selection
    ) => SelectCore(origin, candidates, selection, ownsEvidence: true);

    /// <summary>Separates winner selection from evidence retention and the independently defined cursor.</summary>
    private static SupervisorResolution SelectCore(
        int origin,
        IReadOnlyList<SupervisorCandidate> candidates,
        SupervisorSelection selection,
        bool ownsEvidence
    )
    {
        selection.Validate();
        if (selection.Kind == SupervisorSelectionKind.All)
        {
            return new SupervisorResolution(
                candidates.Count == 0 ? SupervisorResolutionStatus.NotFound : SupervisorResolutionStatus.Found,
                origin,
                candidates,
                null);
        }

        var (winner, ambiguous) = FindWinner(candidates, selection);
        if (winner is null)
        {
            return new(
                ambiguous ? SupervisorResolutionStatus.Ambiguous : SupervisorResolutionStatus.NotFound,
                origin,
                [],
                null);
        }

        var evidenceCount = 0;
        SupervisorCandidate? continuation = null;
        var priorityFirst = selection.Kind == SupervisorSelectionKind.HighestBusinessPriority;

        // WHY: Scan the candidate list directly while counting evidence and selecting the continuation, without an
        // enumerator or intermediate sequence.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < candidates.Count; index++)
        {
            var row = candidates[index];
            if (row.CandidateUserId != winner.CandidateUserId)
            {
                continue;
            }

            evidenceCount++;

            // WHY: A priority-selected person retains all its occurrences, even worse-priority ones. Cursor
            // priority and distance are separate from evidence membership; strict comparison preserves the
            // original first occurrence when both cursor criteria tie.
            if (continuation is null
                || (priorityFirst
                    ? row.LeadershipPriority < continuation.LeadershipPriority
                    || (row.LeadershipPriority == continuation.LeadershipPriority
                        && row.Distance < continuation.Distance)
                    : row.Distance < continuation.Distance))
            {
                continuation = row;
            }
        }

        var evidence = candidates;
        if (!ownsEvidence
            || evidenceCount != candidates.Count)
        {
            // WHY: Ordinary callers retain ownership of their input slots. Allocate their exact winning
            // snapshot, while the owned reader boundary can retain an already complete winner bag directly.
            var retained = new SupervisorCandidate[evidenceCount];
            var retainedIndex = 0;
            // WHY: Fill the exact evidence array in original occurrence order without another filtered collection.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < candidates.Count; index++)
            {
                var row = candidates[index];
                if (row.CandidateUserId == winner.CandidateUserId)
                {
                    retained[retainedIndex++] = row;
                }
            }

            evidence = retained;
        }

        return new SupervisorResolution(SupervisorResolutionStatus.Found, origin, evidence, continuation);
    }

    /// <summary>Finds a unique person without allocating a distinct-person set for uniqueness or priority.</summary>
    private static (SupervisorCandidate? Winner, bool Ambiguous) FindWinner(
        IReadOnlyList<SupervisorCandidate> candidates,
        SupervisorSelection selection
    )
    {
        if (selection.Kind == SupervisorSelectionKind.NthPerson)
        {
            var band = PersonRankBand(candidates, selection.Number!.Value);

            return (band.Length == 1 ? band[0] : null, band.Length > 1);
        }

        SupervisorCandidate? winner = null;
        var ambiguous = false;
        // WHY: Keep winner and tie state in one pass and reject ordinary ambiguity immediately.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < candidates.Count; index++)
        {
            var row = candidates[index];
            if (winner is null
                || (selection.Kind == SupervisorSelectionKind.HighestBusinessPriority
                    && row.LeadershipPriority < winner.LeadershipPriority))
            {
                winner = row;
                ambiguous = false;

                continue;
            }

            if (row.CandidateUserId != winner.CandidateUserId
                && (selection.Kind != SupervisorSelectionKind.HighestBusinessPriority
                    || row.LeadershipPriority == winner.LeadershipPriority))
            {
                if (selection.Kind == SupervisorSelectionKind.RequireUnique)
                {
                    return (null, true);
                }

                // WHY: A later lower priority can still resolve this tie. Priority selection must consume
                // the complete bag, unlike ordinary uniqueness which can reject a second person immediately.
                ambiguous = true;
            }
        }

        return (ambiguous ? null : winner, ambiguous);
    }

    /// <summary>Applies explicit global person uniqueness or application-provided origin priority.</summary>
    internal static SupervisorResolution Across(
        SupervisorResolutionBatch batch,
        AcrossOriginsSelection selection
    )
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(selection);
        ValidateOriginSelection(selection);
        if (!batch.Complete
            || batch.Origins.Any(origin => origin.Status == SupervisorResolutionStatus.LimitExceeded))
        {
            return new SupervisorResolution(SupervisorResolutionStatus.LimitExceeded, null, [], null);
        }

        if (batch.Status != SupervisorBatchStatus.Completed)
        {
            return new SupervisorResolution(
                batch.Status == SupervisorBatchStatus.SourceNotFound
                    ? SupervisorResolutionStatus.SourceNotFound
                    : SupervisorResolutionStatus.NoVisibleMemberships,
                null,
                [],
                null);
        }

        if (selection is FromPrioritizedOrigin priority)
        {
            foreach (var group in priority.GroupIds)
            {
                var origin = batch.Origins.SingleOrDefault(row => row.StartGroupId == group);
                if (origin is not null)
                {
                    if (origin.Status != SupervisorResolutionStatus.Found)
                    {
                        return origin;
                    }

                    var unique = Select(group, origin.Evidence, SupervisorSelection.RequireUnique);

                    // WHY: Origin selection must not replace the cursor already chosen by person-priority semantics.
                    return unique.Status == SupervisorResolutionStatus.Found && origin.Continuation is not null
                        ? origin
                        : unique;
                }
            }

            return new(SupervisorResolutionStatus.NoVisibleMemberships, null, [], null);
        }

        if (batch.Origins.Any(origin => origin.Status == SupervisorResolutionStatus.Ambiguous))
        {
            return new SupervisorResolution(SupervisorResolutionStatus.Ambiguous, null, [], null);
        }

        var evidence = batch
            .Origins
            .SelectMany(origin => origin.Evidence)
            .ToArray();
        var reduced = Select(0, evidence, SupervisorSelection.RequireUnique);

        // WHY: Global person uniqueness does not imply one original path; retain every supporting origin.
        return reduced with
        {
            StartGroupId = null,
            Continuation = null,
        };
    }

    /// <summary>Rejects malformed rules independently of source visibility or partial reader results.</summary>
    private static void ValidateOriginSelection(
        AcrossOriginsSelection selection
    )
    {
        if (selection is FromPrioritizedOrigin priority)
        {
            ArgumentNullException.ThrowIfNull(priority.GroupIds);
            if (priority.GroupIds.Count == 0
                || priority
                    .GroupIds
                    .Distinct()
                    .Count()
                != priority.GroupIds.Count)
            {
                throw new ArgumentException(
                    "Provide an explicit nonempty duplicate-free origin priority.",
                    nameof(selection));
            }

            return;
        }

        if (selection is not RequireUniquePerson)
        {
            throw new ArgumentException("Choose an explicit cross-origin rule.", nameof(selection));
        }
    }

    /// <summary>Finds the requested distinct-person slot while retaining ambiguity for every tied slot.</summary>
    private static SupervisorCandidate[] PersonRankBand(
        IReadOnlyList<SupervisorCandidate> candidates,
        int number
    )
    {
        var canonical = new Dictionary<int, SupervisorCandidate>();

        foreach (var row in candidates)
        {
            if (!canonical.TryGetValue(row.CandidateUserId, out var current)
                || ComparePersonRank(row, current) < 0)
            {
                canonical[row.CandidateUserId] = row;
            }
        }

        if (number > canonical.Count)
        {
            return [];
        }

        // WHY: Only distinct people need ranking. Grouping all occurrences copies high fan-in evidence,
        // while counting every person's predecessors is quadratic in the number of distinct people.
        var people = canonical.Values.ToArray();
        Array.Sort(people, ComparePersonRank);
        var index = number - 1;
        var person = people[index];

        // WHY: Equal ranks occupy adjacent slots. Two representatives suffice for the caller's ambiguity
        // check; a technical identity or unstable sort order must never choose one person from a tied band.
        if (index > 0
            && SameRank(people[index - 1], person))
        {
            return [people[index - 1], person];
        }

        if (index + 1 < people.Length
            && SameRank(person, people[index + 1]))
        {
            return [person, people[index + 1]];
        }

        return [person];
    }

    /// <summary>Orders canonical person ranks without a technical tie-breaker or arithmetic overflow.</summary>
    private static int ComparePersonRank(
        SupervisorCandidate left,
        SupervisorCandidate right
    )
    {
        var distance = left.Distance.CompareTo(right.Distance);

        return distance != 0 ? distance : left.LeadershipPriority.CompareTo(right.LeadershipPriority);
    }

    /// <summary>Identifies the same rank slot group.</summary>
    private static bool SameRank(
        SupervisorCandidate left,
        SupervisorCandidate right
    ) => left.Distance == right.Distance && left.LeadershipPriority == right.LeadershipPriority;
}
