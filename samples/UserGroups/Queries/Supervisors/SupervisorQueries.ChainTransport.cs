namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{
    /// <summary>
    /// Reads bounded batches of distinct physical searches while keeping their logical paths separate.
    /// </summary>
    private static async Task<SupervisorResolutionBatch[]?> ReadFrontierAsync(
        UserGroupContext context,
        Guid tenantId,
        List<(SupervisorRequest Request, SupervisorSelection Selection)> searches,
        ChainBudget budget,
        CancellationToken cancellationToken
    )
    {
        var results = new SupervisorResolutionBatch[searches.Count];
        // WHY: Four UNION branches bound SQL/parameter growth even with sixteen metadata matches per state.
        // The provider tests inspect this actual transport boundary instead of relying on a nominal engine maximum.
        const int statesPerBatch = 4;
        for (var offset = 0; offset < searches.Count; offset += statesPerBatch)
        {
            IQueryable<SupervisorReadRow>? query = null;
            var count = Math.Min(statesPerBatch, searches.Count - offset);
            // WHY: Bind each bounded UNION branch to its stable batch-local state ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < count; index++)
            {
                var ordinal = index;
                var (request, selection) = searches[offset + index];
                var part = ResolutionRows(context, tenantId, request, selection)
                    .Select(row => new SupervisorReadRow
                    {
                        StateOrdinal = ordinal,
                        Kind = row.Kind,
                        HasEvidence = row.HasEvidence,
                        CatalogValid = row.CatalogValid,
                        SourceUserId = row.SourceUserId,
                        StartGroupId = row.StartGroupId,
                        StartTreeId = row.StartTreeId,
                        Count = row.Count,
                        FoundGroupId = row.FoundGroupId,
                        CandidateUserId = row.CandidateUserId,
                        Distance = row.Distance,
                        LeadershipPriority = row.LeadershipPriority,
                    });

                query = query is null ? part : query.Concat(part);
            }

            var remaining = budget.RemainingReads;
            if (remaining == 0)
            {
                return null;
            }

            // WHY: Retrying EF strategies may buffer a reader internally. Apply the overflow probe in SQL,
            // after all ranking and selection, so even that buffer is bounded before our async enumerator runs.
            var ordered = query!
                .OrderBy(row => row.StateOrdinal)
                .ThenBy(row => row.Kind)
                .ThenBy(row => row.SourceUserId)
                .ThenBy(row => row.StartGroupId)
                .ThenBy(row => row.FoundGroupId)
                .ThenBy(row => row.CandidateUserId);

            var bounded = ordered.Take(remaining + 1);

            var rows = Enumerable
                .Range(0, count)
                .Select(_ => new List<SupervisorReadRow>())
                .ToArray();

            await foreach (var row in bounded
                               .AsAsyncEnumerable()
                               .WithCancellation(cancellationToken))
            {
                if (!budget.Read())
                {
                    return null;
                }

                rows[row.StateOrdinal]
                    .Add(row);
            }

            // WHY: Match each returned row bucket to the same position in the original search batch.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < count; index++)
            {
                results[offset + index] = ReadResolution(tenantId, rows[index], searches[offset + index].Selection);
            }
        }

        return results;
    }
}
