namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{
    /// <summary>Reads the explicitly requested server-selected evidence in one asynchronous operation.</summary>
    private static async Task<SupervisorResolutionBatch> ResolveCoreAsync(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        SupervisorSelection selection,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = ResolutionRows(context, tenantId, request, selection);
        var read = new List<SupervisorReadRow>();
        await foreach (var row in rows
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            read.Add(row);
        }

        return ReadResolution(tenantId, read, selection);
    }

    /// <summary>Reconstructs server-selected scalar evidence without loading hierarchy entities.</summary>
    internal static SupervisorResolutionBatch ReadResolution(
        Guid tenantId,
        IReadOnlyList<SupervisorReadRow> rows,
        SupervisorSelection selection
    )
    {
        var source = rows.FirstOrDefault(row => row.Kind == 0);
        if (source is null)
        {
            return new(SupervisorBatchStatus.SourceNotFound, []);
        }

        if (!source.CatalogValid)
        {
            throw new ArgumentException(
                "The requested role, group type, or Text metadata definition is not visible in this tenant.");
        }

        var origins = new List<SupervisorResolution>();

        foreach (var origin in rows
                     .Where(row => row.Kind == 1)
                     .GroupBy(row => row.StartGroupId))
        {
            var summary = origin.First();
            if (selection.Kind != SupervisorSelectionKind.All
                && summary.Count > 1)
            {
                origins.Add(new SupervisorResolution(SupervisorResolutionStatus.Ambiguous, summary.StartGroupId, [], null));

                continue;
            }

            var selected = origin
                .Where(row => row.HasEvidence)
                .Select(row => new SupervisorCandidate
                {
                    TenantId = tenantId,
                    SourceUserId = row.SourceUserId,
                    StartGroupId = row.StartGroupId,
                    StartTreeId = row.StartTreeId,
                    FoundGroupId = row.FoundGroupId,
                    CandidateUserId = row.CandidateUserId,
                    Distance = row.Distance,
                    LeadershipPriority = row.LeadershipPriority,
                })
                .ToArray();

            // WHY: This fresh array belongs only to this reconstruction. Transfer that ownership explicitly
            // so a server-proven winner does not require another occurrence-sized evidence snapshot.
            origins.Add(
                SupervisorSelectionRules.SelectOwned(
                    summary.StartGroupId,
                    selected,
                    selection.Kind == SupervisorSelectionKind.NthPerson
                        ? SupervisorSelection.RequireUnique
                        : selection));
        }

        return new SupervisorResolutionBatch(
            origins.Count == 0 ? SupervisorBatchStatus.NoVisibleMemberships : SupervisorBatchStatus.Completed,
            origins);
    }
}
