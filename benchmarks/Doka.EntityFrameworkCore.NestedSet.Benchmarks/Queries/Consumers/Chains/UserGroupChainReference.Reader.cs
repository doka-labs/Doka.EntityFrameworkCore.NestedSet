using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Retains bounded server person reduction in the independent per-path chain comparison.</summary>
internal static partial class UserGroupChainReference
{
    /// <summary>Streams source, origin status, and selected evidence without buffering candidate fan-in.</summary>
    private static async Task<SupervisorResolutionBatch?> ReadAsync(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        SupervisorSelection selection,
        ReferenceBudget budget,
        CancellationToken cancellationToken
    )
    {
        var sourceSeen = false;
        var summaries = new List<ReferenceRow>();
        var evidence = new Dictionary<int, List<SupervisorCandidate>>();

        await foreach (var row in Rows(context, tenantId, request, selection)
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            if (!budget.Read())
            {
                return null;
            }

            switch (row.Kind)
            {
                case 0:
                    if (!row.CatalogValid)
                    {
                        throw new ArgumentException("The requested sample catalog is not visible in this tenant.");
                    }

                    sourceSeen = true;
                    break;
                case 1:
                    summaries.Add(row);
                    break;
                case 2:
                    if (!evidence.TryGetValue(row.StartGroupId, out var group))
                    {
                        group = [];
                        evidence.Add(row.StartGroupId, group);
                    }

                    group.Add(
                        new SupervisorCandidate
                        {
                            TenantId = tenantId,
                            SourceUserId = request.SourceUserId,
                            StartGroupId = row.StartGroupId,
                            StartTreeId = row.StartTreeId,
                            FoundGroupId = row.FoundGroupId,
                            CandidateUserId = row.CandidateUserId,
                            Distance = row.Distance,
                            LeadershipPriority = row.LeadershipPriority,
                        });
                    break;
            }
        }

        if (!sourceSeen)
        {
            return new SupervisorResolutionBatch(SupervisorBatchStatus.SourceNotFound, []);
        }

        var origins = new List<SupervisorResolution>(summaries.Count);

        foreach (var summary in summaries)
        {
            if (selection.Kind != SupervisorSelectionKind.All
                && summary.Count > 1)
            {
                origins.Add(new(SupervisorResolutionStatus.Ambiguous, summary.StartGroupId, [], null));

                continue;
            }

            IReadOnlyList<SupervisorCandidate> candidates = evidence.GetValueOrDefault(summary.StartGroupId) ?? [];
            origins.Add(
                SupervisorSelectionRules.Select(
                    summary.StartGroupId,
                    candidates,
                    selection.Kind == SupervisorSelectionKind.NthPerson
                        ? SupervisorSelection.RequireUnique
                        : selection));
        }

        return new SupervisorResolutionBatch(
            origins.Count == 0 ? SupervisorBatchStatus.NoVisibleMemberships : SupervisorBatchStatus.Completed,
            origins);
    }

    /// <summary>Builds one bounded per-path reader over the same authorized structural candidate contract.</summary>
    private static IQueryable<ReferenceRow> Rows(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        SupervisorSelection selection
    )
    {
        SupervisorRequestValidation.Validate(request);
        selection.Validate();
        var candidates = SupervisorQueries.Candidates(context, tenantId, request);
        var pool = SelectPeople(candidates, selection);
        var source = request.SourceUserId;
        var start = request.StartGroupId;
        var day = request.OnDay;
        var role = request.Roles switch
        {
            MembershipRoleFilter local => (Guid?)local.RoleId,
            GlobalUserRole global => global.RoleId,
            EitherRole either => either.RoleId,
            _ => null,
        };

        var types = request.Groups switch
        {
            AllEligibleGroups all => all
                .Types
                .Distinct()
                .ToArray(),
            NthEligibleGroup nth => nth
                .Types
                .Distinct()
                .ToArray(),
            NthTypeGroupThenValidate typed => [typed.TypeId],
            _ => [],
        };

        var userFields = RequestedFields(request, MetadataOwner.SourceUser, MetadataOwner.CandidateUser);
        var memberFields = RequestedFields(request, MetadataOwner.SourceMembership, MetadataOwner.CandidateMembership);
        var groupFields = RequestedFields(request, MetadataOwner.SourceGroup, MetadataOwner.CandidateGroup);
        var status = context
            .Users
            .Where(user => user.TenantId == tenantId && user.Id == source)
            .Select(user => new ReferenceRow
            {
                Kind = 0,
                CatalogValid =
                    (role == null || context.Roles.Any(value => value.TenantId == tenantId && value.Id == role))
                    && (types.Length == 0
                        || context.GroupTypes.LongCount(value => value.TenantId == tenantId
                            && Enumerable.Contains(types, value.Id))
                        == types.Length)
                    && (userFields.Length == 0
                        || context.UserMetadataDefinitions.LongCount(value =>
                            value.TenantId == tenantId
                            && Enumerable.Contains(userFields, value.FieldCode)
                            && value.ValueKind == "Text")
                        == userFields.Length)
                    && (memberFields.Length == 0
                        || context
                            .MembershipMetadataDefinitions
                            .Where(value =>
                                value.TenantId == tenantId
                                && Enumerable.Contains(memberFields, value.FieldCode)
                                && value.ValueKind == "Text")
                            .Select(value => value.FieldCode)
                            .Distinct()
                            .LongCount()
                        == memberFields.Length)
                    && !context.MembershipMetadataDefinitions.Any(value => value.TenantId == tenantId
                        && Enumerable.Contains(memberFields, value.FieldCode)
                        && value.ValueKind != "Text")
                    && (groupFields.Length == 0
                        || context
                            .GroupMetadataDefinitions
                            .Where(value =>
                                value.TenantId == tenantId
                                && Enumerable.Contains(groupFields, value.FieldCode)
                                && value.ValueKind == "Text")
                            .Select(value => value.FieldCode)
                            .Distinct()
                            .LongCount()
                        == groupFields.Length)
                    && !context.GroupMetadataDefinitions.Any(value => value.TenantId == tenantId
                        && Enumerable.Contains(groupFields, value.FieldCode)
                        && value.ValueKind != "Text"),
                StartGroupId = 0,
                StartTreeId = Guid.Empty,
                Count = 0,
                FoundGroupId = 0,
                CandidateUserId = 0,
                Distance = 0,
                LeadershipPriority = 0,
            });

        var origins = context
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
                (member, group) => new ReferenceRow
                {
                    Kind = 1,
                    CatalogValid = false,
                    StartGroupId = group.Id,
                    StartTreeId = group.TreeId,
                    Count = pool
                        .Where(person => person.StartGroupId == group.Id)
                        .Select(person => person.CandidateUserId)
                        .Distinct()
                        .LongCount(),
                    FoundGroupId = 0,
                    CandidateUserId = 0,
                    Distance = 0,
                    LeadershipPriority = 0,
                });

        // WHY: Reusing the All reader and reducing after materialization changes both memory behavior and
        // low-budget ambiguity into overflow. Reduce distinct people on the server before any evidence is read.
        var selected = selection.Kind == SupervisorSelectionKind.All
            ? candidates
            : candidates.Where(row => pool
                    .Where(person => person.StartGroupId == row.StartGroupId)
                    .Select(person => person.CandidateUserId)
                    .Distinct()
                    .LongCount()
                == 1
                && pool.Any(person => person.StartGroupId == row.StartGroupId
                    && person.CandidateUserId == row.CandidateUserId));

        var evidence = selected.Select(row => new ReferenceRow
        {
            Kind = 2,
            CatalogValid = false,
            Count = 0,
            StartGroupId = row.StartGroupId,
            StartTreeId = row.StartTreeId,
            FoundGroupId = row.FoundGroupId,
            CandidateUserId = row.CandidateUserId,
            Distance = row.Distance,
            LeadershipPriority = row.LeadershipPriority,
        });

        return status
            .Concat(origins)
            .Concat(evidence);
    }

    /// <summary>Preserves server uniqueness, business-priority ties, and occupied person-rank slots.</summary>
    private static IQueryable<SupervisorCandidate> SelectPeople(
        IQueryable<SupervisorCandidate> candidates,
        SupervisorSelection selection
    )
    {
        if (selection.Kind == SupervisorSelectionKind.HighestBusinessPriority)
        {
            return candidates.Where(row => !candidates.Any(other => other.StartGroupId == row.StartGroupId
                && other.LeadershipPriority < row.LeadershipPriority));
        }

        if (selection.Kind != SupervisorSelectionKind.NthPerson)
        {
            return candidates;
        }

        var people = candidates.Where(row => !candidates.Any(other => other.StartGroupId == row.StartGroupId
            && other.CandidateUserId == row.CandidateUserId
            && other.Distance < row.Distance));

        var number = selection.Number!.Value;

        return people.Where(row =>
            people.LongCount(other =>
                other.StartGroupId == row.StartGroupId
                && (other.Distance < row.Distance
                    || (other.Distance == row.Distance && other.LeadershipPriority < row.LeadershipPriority)))
            < number
            && people.LongCount(other => other.StartGroupId == row.StartGroupId
                && (other.Distance < row.Distance
                    || (other.Distance == row.Distance && other.LeadershipPriority <= row.LeadershipPriority)))
            >= number);
    }

    /// <summary>Validates the exact requested physical metadata catalogs in the same status reader.</summary>
    private static string[] RequestedFields(
        SupervisorRequest request,
        MetadataOwner source,
        MetadataOwner candidate
    ) => request
        .Matches
        .SelectMany(match => new[]
        {
            match.SourceOwner == source ? match.SourceFieldCode : null,
            match.CandidateOwner == candidate ? match.CandidateFieldCode : null,
        })
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Shares one scalar union shape for source, per-origin status, and selected evidence.</summary>
    private sealed class ReferenceRow
    {
        /// <summary>Gets the source/status/evidence discriminator.</summary>
        public int Kind { get; init; }

        /// <summary>Gets catalog validity without a preloaded source user.</summary>
        public bool CatalogValid { get; init; }

        /// <summary>Gets the exact current membership group.</summary>
        public int StartGroupId { get; init; }

        /// <summary>Gets the current path's stable tree.</summary>
        public Guid StartTreeId { get; init; }

        /// <summary>Gets the selected distinct-person count.</summary>
        public long Count { get; init; }

        /// <summary>Gets the selected candidate membership group.</summary>
        public int FoundGroupId { get; init; }

        /// <summary>Gets the selected person.</summary>
        public int CandidateUserId { get; init; }

        /// <summary>Gets stored structural distance.</summary>
        public int Distance { get; init; }

        /// <summary>Gets the membership's business priority.</summary>
        public int LeadershipPriority { get; init; }
    }
}
