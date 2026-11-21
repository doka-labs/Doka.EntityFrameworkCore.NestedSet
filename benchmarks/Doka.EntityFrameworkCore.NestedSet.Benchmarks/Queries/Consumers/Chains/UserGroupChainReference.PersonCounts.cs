using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

internal static partial class UserGroupChainReference
{
    /// <summary>Counts distinct selected people before any origin or evidence row is correlated.</summary>
    private static IQueryable<ReferencePersonCount> CountPeople(
        IQueryable<SupervisorCandidate> pool
    ) => pool
        .GroupBy(person => person.StartGroupId)
        .Select(group => new ReferencePersonCount
        {
            StartGroupId = group.Key,
            Count = group.Select(person => person.CandidateUserId).Distinct().LongCount(),
            // WHY: Only Count == 1 admits this identity; MIN never chooses a winner among different people.
            PersonId = group.Min(person => person.CandidateUserId),
        });

    /// <summary>Retains every authorized origin, including those without a selected candidate.</summary>
    private static IQueryable<ReferenceRow> OriginRows(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        IQueryable<ReferencePersonCount> people
    )
    {
        var source = request.SourceUserId;
        var start = request.StartGroupId;
        var day = request.OnDay;

        // WHY: Grouping before the join keeps MariaDB from resolving an outer origin inside a DISTINCT table.
        return context
            .UserGroupMemberships
            .Where(member => member.TenantId == tenantId
                && member.UserId == source
                && (start == null || member.UserGroupId == start)
                && context.Users.Any(user => user.TenantId == tenantId && user.Id == source)
                && (day == null
                    || ((member.FromDate == null || member.FromDate <= day)
                        && (member.ToDate == null || member.ToDate >= day))))
            .Join(
                context.Groups.Where(group => group.TenantId == tenantId),
                member => member.UserGroupId,
                group => group.Id,
                (member, group) => new
                {
                    StartGroupId = group.Id,
                    StartTreeId = group.TreeId,
                })
            .GroupJoin(
                people,
                origin => origin.StartGroupId,
                person => person.StartGroupId,
                (origin, matching) => new { origin, matching })
            .SelectMany(
                row => row.matching.DefaultIfEmpty(),
                (row, person) => new ReferenceRow
                {
                    Kind = 1,
                    CatalogValid = false,
                    StartGroupId = row.origin.StartGroupId,
                    StartTreeId = row.origin.StartTreeId,
                    Count = (long?)person!.Count ?? 0L,
                    FoundGroupId = 0,
                    CandidateUserId = 0,
                    Distance = 0,
                    LeadershipPriority = 0,
                });
    }

    /// <summary>Retains the person count and an identity usable only when that count proves uniqueness.</summary>
    private sealed class ReferencePersonCount
    {
        /// <summary>Gets the exact authorized origin membership.</summary>
        public int StartGroupId { get; init; }

        /// <summary>Gets the distinct selected-person count before evidence is read.</summary>
        public long Count { get; init; }

        /// <summary>Gets the sole selected identity when Count equals one.</summary>
        public int PersonId { get; init; }
    }
}
