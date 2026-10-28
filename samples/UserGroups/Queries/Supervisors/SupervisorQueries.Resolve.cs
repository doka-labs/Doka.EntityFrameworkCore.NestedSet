namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Composes server-side source status, catalog validity, and selected supervisor evidence.</summary>
internal static partial class SupervisorQueries
{
    /// <summary>Builds the status/evidence union used by one-hop calls and bounded chain batches.</summary>
    internal static IQueryable<SupervisorReadRow> ResolutionRows(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        SupervisorSelection selection
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        SupervisorRequestValidation.Validate(request);
        selection.Validate();
        var source = request.SourceUserId;
        var memberships = SourceMemberships(context, tenantId, request);
        var origins = Origins(context, tenantId, source, memberships);

        // WHY: Status and candidate composition share one exact origin expression. Rebuilding it also
        // repeated complete request validation, although this entry point has already validated the request.
        var candidates = BuildCandidates(context, tenantId, request, origins, memberships);
        var roleId = request.Roles switch
        {
            MembershipRoleFilter local => (Guid?)local.RoleId,
            GlobalUserRole global => global.RoleId,
            EitherRole either => either.RoleId,
            _ => null,
        };

        var requiredTypes = request.Groups switch
        {
            AllEligibleGroups all => all.Types.Distinct().ToArray(),
            NthEligibleGroup nth => nth.Types.Distinct().ToArray(),
            NthTypeGroupThenValidate typed => [typed.TypeId],
            _ => [],
        };

        var userFields = Fields(request.Matches, MetadataOwner.SourceUser, MetadataOwner.CandidateUser);
        var membershipFields = Fields(
            request.Matches, MetadataOwner.SourceMembership, MetadataOwner.CandidateMembership);

        var groupFields = Fields(request.Matches, MetadataOwner.SourceGroup, MetadataOwner.CandidateGroup);

        // WHY: EF 10 otherwise expands catalog Contains into padded scalar parameters. Each catalog
        // array uses the providers' existing JSON/array transport so growing the criterion count does
        // not grow the SQL/binding list. The override is local to these finite catalog checks.
        var sourceRows = context.Users.Where(user => user.TenantId == tenantId && user.Id == source)
            .Select(user => new SupervisorReadRow
            {
                Kind = 0, HasEvidence = false,
                CatalogValid = (roleId == null || context.Roles.Any(role =>
                        role.TenantId == tenantId && role.Id == roleId))
                    && (requiredTypes.Length == 0 || context.GroupTypes.LongCount(type =>
                        type.TenantId == tenantId && Enumerable.Contains(EF.Parameter(requiredTypes), type.Id))
                        == requiredTypes.Length)
                    && (userFields.Length == 0 || context.UserMetadataDefinitions.LongCount(definition =>
                        definition.TenantId == tenantId
                        && Enumerable.Contains(EF.Parameter(userFields),
                            SupervisorExactText.Suffix(definition.FieldCode))
                        && SupervisorExactText.FieldEquals(definition.ValueKind, "Text")) == userFields.Length)
                    && (membershipFields.Length == 0 || context.MembershipMetadataDefinitions
                        .Where(definition => definition.TenantId == tenantId
                            && Enumerable.Contains(EF.Parameter(membershipFields),
                                SupervisorExactText.Suffix(definition.FieldCode))
                            && SupervisorExactText.FieldEquals(definition.ValueKind, "Text"))
                        .Select(definition => definition.FieldCode).Distinct().LongCount() == membershipFields.Length)
                    && !context.MembershipMetadataDefinitions.Any(definition => definition.TenantId == tenantId
                        && Enumerable.Contains(EF.Parameter(membershipFields),
                            SupervisorExactText.Suffix(definition.FieldCode))
                        && !SupervisorExactText.FieldEquals(definition.ValueKind, "Text"))
                    && (groupFields.Length == 0 || context.GroupMetadataDefinitions
                        .Where(definition => definition.TenantId == tenantId
                            && Enumerable.Contains(EF.Parameter(groupFields),
                                SupervisorExactText.Suffix(definition.FieldCode))
                            && SupervisorExactText.FieldEquals(definition.ValueKind, "Text"))
                        .Select(definition => definition.FieldCode).Distinct().LongCount() == groupFields.Length)
                    && !context.GroupMetadataDefinitions.Any(definition => definition.TenantId == tenantId
                        && Enumerable.Contains(EF.Parameter(groupFields),
                            SupervisorExactText.Suffix(definition.FieldCode))
                        && !SupervisorExactText.FieldEquals(definition.ValueKind, "Text")),
                StateOrdinal = 0, SourceUserId = user.Id, StartGroupId = 0, StartTreeId = Guid.Empty,
                Count = 0, FoundGroupId = 0, CandidateUserId = 0, Distance = 0, LeadershipPriority = 0,
            });

        sourceRows = sourceRows.Provider.CreateQuery<SupervisorReadRow>(
            SupervisorExactText.Inline(sourceRows.Expression));

        if (selection.Kind == SupervisorSelectionKind.All)
        {
            // WHY: Every origin needs an explicit status even without evidence. Independent markers and
            // evidence avoid a LEFT JOIN that PostgreSQL repeatedly scanned for every origin. These real
            // marker rows still consume the chain's unchanged physical transport budget.
            // WHY: The authorized origin already proves this exact source user. Reading its membership
            // key keeps the cached marker expression request-free without bypassing source visibility.
            var originMarkers = origins.Select(s_originMarkerProjection);
            var evidenceRows = candidates.Select(s_evidenceRowProjection);

            return sourceRows.Concat(originMarkers).Concat(evidenceRows);
        }

        // WHY: Equal minimum and maximum IDs prove exactly one distinct person, even with many occurrences.
        // Unequal IDs mean ambiguity; neither extreme chooses a business winner. This avoids a DISTINCT set
        // and keeps the grouped relation uncorrelated for MariaDB's derived-table restrictions.
        var witnesses = PersonWitnesses(candidates, origins, selection);
        var outcomes = origins.GroupJoin(witnesses, origin => origin.Group.Id, witness => witness.StartGroupId,
                (origin, matching) => new { origin, matching })
            .SelectMany(row => row.matching.DefaultIfEmpty(), (row, witness) => new SupervisorOriginOutcome
            {
                // WHY: EF lifts this LEFT JOIN scalar to SQL NULL; this coalesce never dereferences it in CLR code.
                Origin = row.origin,
                Count = witness!.Minimum == null ? 0L : witness.Minimum == witness.Maximum ? 1L : 2L,
                PersonId = witness.Minimum == witness.Maximum ? witness.Minimum : null,
                FoundGroupId = witness.FoundGroupId,
                Distance = witness.Distance,
                LeadershipPriority = witness.LeadershipPriority,
            });

        if (request.Groups is NthTypeGroupThenValidate or NthEligibleGroup)
        {
            // WHY: Both group windows admit at most one found group per origin. The exact membership key
            // admits one occurrence of a person in that group, so a unique witness already contains every
            // evidence field. Rejoining the full candidate relation would repeat routing and path checks.
            // WHY: The outer origin remains present for missing or ambiguous witnesses. Its exact
            // membership carries the already authorized source key even when PersonId is SQL NULL.
            var singleGroupRows = outcomes.Select(s_singleGroupRowProjection);

            return sourceRows.Concat(singleGroupRows);
        }

        // WHY: Keep complete evidence separate from the outcome LEFT JOIN. Otherwise MySQL joined every
        // membership of a proven unique person to every origin before applying its structural rank and path.
        // Source/member keys and EXISTS predicates make (origin, found group, person) unique; DISTINCT retains
        // all valid occurrences, including the same person in different found groups.
        var admittedEvidence = candidates.Select(candidate => new
        {
            candidate.StartGroupId,
            CandidateUserId = (int?)candidate.CandidateUserId,
            FoundGroupId = (int?)candidate.FoundGroupId,
            Distance = (int?)candidate.Distance,
            LeadershipPriority = (int?)candidate.LeadershipPriority,
        }).Distinct();

        // WHY: Join the outcome only once, then null-extend its admitted evidence. An ambiguous 10,000-person
        // fan-in emits one outcome row; a unique person retains every original occurrence for cursor selection.
        var joined = outcomes.GroupJoin(admittedEvidence,
            outcome => new { StartGroupId = outcome.Origin.Group.Id, outcome.PersonId },
            candidate => new { candidate.StartGroupId, PersonId = candidate.CandidateUserId },
            (outcome, matching) => new { outcome, matching });

        var originRows = joined
            .SelectMany(row => row.matching.DefaultIfEmpty(), (row, candidate) => new SupervisorReadRow
            {
                Kind = 1, CatalogValid = false, StateOrdinal = 0,
                HasEvidence = candidate!.CandidateUserId != null,
                Count = row.outcome.Count,
                SourceUserId = source,
                StartGroupId = row.outcome.Origin.Group.Id,
                StartTreeId = row.outcome.Origin.Group.TreeId,
                FoundGroupId = candidate.FoundGroupId ?? 0,
                CandidateUserId = candidate.CandidateUserId ?? 0,
                Distance = candidate.Distance ?? 0,
                LeadershipPriority = candidate.LeadershipPriority ?? 0,
            });

        return sourceRows.Concat(originRows);
    }

    /// <summary>Collects only requested fields for one explicit physical owner catalog.</summary>
    private static string[] Fields(IReadOnlyList<MetadataMatch> matches, MetadataOwner source, MetadataOwner candidate)
        => matches.SelectMany(match => new[]
            {
                match.SourceOwner == source ? match.SourceFieldCode : null,
                match.CandidateOwner == candidate ? match.CandidateFieldCode : null,
            })
            .OfType<string>().Distinct(StringComparer.Ordinal).Select(SupervisorExactText.Suffix).ToArray();
}
