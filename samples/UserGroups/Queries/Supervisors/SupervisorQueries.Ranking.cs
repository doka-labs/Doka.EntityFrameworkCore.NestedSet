namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{
    /// <summary>
    /// Selects the ranked occurrences whose per-origin aggregate is the server witness, including exact occurrence
    /// fields for single-group rules.
    /// </summary>
    private static IQueryable<SupervisorCandidate> WitnessOccurrences(
        IQueryable<SupervisorCandidate> candidates,
        IQueryable<SupervisorOrigin> origins,
        SupervisorSelection selection
    )
    {
        if (selection.Kind == SupervisorSelectionKind.HighestBusinessPriority)
        {
            var minima = candidates
                .GroupBy(row => row.StartGroupId)
                .Select(group => new
                {
                    StartGroupId = group.Key,
                    Priority = group.Min(row => row.LeadershipPriority),
                });

            // WHY: An uncorrelated aggregate and equality join avoid probing the full candidate relation for every
            // better-priority comparison. Every person at the best priority remains admitted, including ties.
            return candidates.Join(
                minima,
                row => new
                {
                    row.StartGroupId,
                    Priority = row.LeadershipPriority,
                },
                minimum => new
                {
                    minimum.StartGroupId,
                    minimum.Priority,
                },
                (row, minimum) => row);
        }

        if (selection.Kind != SupervisorSelectionKind.NthPerson)
        {
            return candidates;
        }

        // WHY: Distance is a nonnegative Int32 and priority is a signed Int32. Shifting distance by 32 bits and
        // offsetting priority losslessly represents their lexicographic order in [0, Int64.MaxValue]. The cast
        // precedes multiplication. MIN therefore keeps each person's nearest distance and its best priority,
        // without repeated candidate anti-joins or a technical user-ID tie-breaker.
        // WHY: The origin group determines tenant, tree and source person; keying them keeps each ranked
        // person a complete candidate for the per-origin outcome.
        var ranks = candidates
            .GroupBy(row => new
            {
                row.TenantId,
                row.SourceUserId,
                row.StartGroupId,
                row.StartTreeId,
                row.CandidateUserId
            })
            .Select(group => new
            {
                group.Key.TenantId,
                group.Key.SourceUserId,
                group.Key.StartGroupId,
                group.Key.StartTreeId,
                group.Key.CandidateUserId,
                Rank = group.Min(row => row.Distance * 4_294_967_296L + ((long)row.LeadershipPriority - int.MinValue)),
                FoundGroupId = group.Min(row => row.FoundGroupId),
                Distance = group.Min(row => row.Distance),
                LeadershipPriority = group.Min(row => row.LeadershipPriority),
            });

        var number = selection.Number!.Value;

        // WHY: Choose one rank per visible origin, not once for every canonical person. DISTINCT expresses an
        // explicit origin/rank relation; flattening the previous projection into the person join repeated the
        // complete ranked search for every person. Native work counters guard that execution boundary.
        // Nullable absence coalesces to -1, outside the valid rank domain.
        var originRanks = origins
            .Select(origin => new
            {
                StartGroupId = origin.Group.Id,
                Rank = ranks
                        .Where(other => other.StartGroupId == origin.Group.Id)
                        .OrderBy(other => other.Rank)
                        .Skip(number - 1)
                        .Select(other => (long?)other.Rank)
                        .FirstOrDefault()
                    ?? -1L,
            })
            .Distinct();

        // WHY: Join the rank value rather than a technical identity so every tied person remains admitted.
        return originRanks.Join(
            ranks,
            origin => new
            {
                origin.StartGroupId,
                origin.Rank,
            },
            person => new
            {
                person.StartGroupId,
                person.Rank,
            },
            (origin, person) => new SupervisorCandidate
            {
                TenantId = person.TenantId,
                SourceUserId = person.SourceUserId,
                StartGroupId = person.StartGroupId,
                StartTreeId = person.StartTreeId,
                FoundGroupId = person.FoundGroupId,
                CandidateUserId = person.CandidateUserId,
                Distance = person.Distance,
                LeadershipPriority = person.LeadershipPriority,
            });
    }

    /// <summary>Proves zero, one, or multiple distinct people without transporting the complete person set.</summary>
    private static IQueryable<SupervisorPersonWitness> Witnesses(
        IQueryable<SupervisorCandidate> candidates
    ) => candidates
        .GroupBy(s_candidateOriginKey)
        .Select(s_personWitnessProjection);
}
