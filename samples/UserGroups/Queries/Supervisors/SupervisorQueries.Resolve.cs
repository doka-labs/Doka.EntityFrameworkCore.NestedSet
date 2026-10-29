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

        var userFields = Fields(request.Matches, MetadataOwner.SourceUser, MetadataOwner.CandidateUser);
        var membershipFields = Fields(
            request.Matches,
            MetadataOwner.SourceMembership,
            MetadataOwner.CandidateMembership);

        var groupFields = Fields(request.Matches, MetadataOwner.SourceGroup, MetadataOwner.CandidateGroup);

        // WHY: EF 10 otherwise expands catalog Contains into padded scalar parameters. Each catalog
        // array uses the providers' existing JSON/array transport so growing the criterion count does
        // not grow the SQL/binding list. The override is local to these finite catalog checks.
        var sourceRows = context
            .Users
            .Where(user => user.TenantId == tenantId && user.Id == source)
            .Select(user => new SupervisorReadRow
            {
                Kind = 0,
                HasEvidence = false,
                CatalogValid =
                    (roleId == null || context.Roles.Any(role => role.TenantId == tenantId && role.Id == roleId))
                    && (requiredTypes.Length == 0
                        || context.GroupTypes.LongCount(type =>
                            type.TenantId == tenantId && Enumerable.Contains(EF.Parameter(requiredTypes), type.Id))
                        == requiredTypes.Length)
                    && (userFields.Length == 0
                        || context.UserMetadataDefinitions.LongCount(definition => definition.TenantId == tenantId
                            && Enumerable.Contains(
                                EF.Parameter(userFields),
                                // WHY: The text inliner expands this suffix to concatenation before EF translation.
                                // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                                SupervisorExactText.Suffix(definition.FieldCode))
                            // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                            // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                            && SupervisorExactText.FieldEquals(definition.ValueKind, "Text"))
                        == userFields.Length)
                    && (membershipFields.Length == 0
                        || context
                            .MembershipMetadataDefinitions
                            .Where(definition => definition.TenantId == tenantId
                                && Enumerable.Contains(
                                    EF.Parameter(membershipFields),
                                    // WHY: The text inliner expands this suffix to concatenation before EF translation.
                                    // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                                    SupervisorExactText.Suffix(definition.FieldCode))
                                // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                                // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                                && SupervisorExactText.FieldEquals(definition.ValueKind, "Text"))
                            .Select(definition => definition.FieldCode)
                            .Distinct()
                            .LongCount()
                        == membershipFields.Length)
                    && !context.MembershipMetadataDefinitions.Any(definition => definition.TenantId == tenantId
                        && Enumerable.Contains(
                            EF.Parameter(membershipFields),
                            // WHY: The text inliner expands this suffix to concatenation before EF translation.
                            // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                            SupervisorExactText.Suffix(definition.FieldCode))
                        // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                        && !SupervisorExactText.FieldEquals(definition.ValueKind, "Text"))
                    && (groupFields.Length == 0
                        || context
                            .GroupMetadataDefinitions
                            .Where(definition => definition.TenantId == tenantId
                                && Enumerable.Contains(
                                    EF.Parameter(groupFields),
                                    // WHY: The text inliner expands this suffix to concatenation before EF translation.
                                    // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                                    SupervisorExactText.Suffix(definition.FieldCode))
                                // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                                // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                                && SupervisorExactText.FieldEquals(definition.ValueKind, "Text"))
                            .Select(definition => definition.FieldCode)
                            .Distinct()
                            .LongCount()
                        == groupFields.Length)
                    && !context.GroupMetadataDefinitions.Any(definition => definition.TenantId == tenantId
                        && Enumerable.Contains(
                            EF.Parameter(groupFields),
                            // WHY: The text inliner expands this suffix to concatenation before EF translation.
                            // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                            SupervisorExactText.Suffix(definition.FieldCode))
                        // WHY: The text inliner replaces this marker with string comparisons before EF translation.
                        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                        && !SupervisorExactText.FieldEquals(definition.ValueKind, "Text")),
                StateOrdinal = 0,
                SourceUserId = user.Id,
                StartGroupId = 0,
                StartTreeId = Guid.Empty,
                Count = 0,
                FoundGroupId = 0,
                CandidateUserId = 0,
                Distance = 0,
                LeadershipPriority = 0,
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

            return sourceRows
                .Concat(originMarkers)
                .Concat(evidenceRows);
        }

        // WHY: Equal minimum and maximum IDs prove exactly one distinct person, even with many occurrences.
        // Unequal IDs mean ambiguity; neither extreme chooses a business winner. This avoids a DISTINCT set
        // and keeps the grouped relation uncorrelated for MariaDB's derived-table restrictions.
        var witnesses = PersonWitnesses(candidates, origins, selection);

        if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            // WHY: SQL Server pushed each origin's group key into this grouped relation and rebuilt every
            // ancestor path per origin. Behind OFFSET 0 the measured plans completed the witnesses once.
            witnesses = witnesses.OrderBy(witness => witness.StartGroupId).Skip(0);
        }

        var outcomes = origins
            .GroupJoin(
                witnesses,
                origin => origin.Group.Id,
                witness => witness.StartGroupId,
                (origin, matching) => new
                {
                    origin,
                    matching
                })
            .SelectMany(
                row => row.matching.DefaultIfEmpty(),
                (row, witness) => new SupervisorOriginOutcome
                {
                    // WHY: EF lifts this LEFT JOIN scalar to SQL NULL; this coalesce never dereferences it in CLR code.
                    Origin = row.origin,
                    Count = witness!.Minimum == null
                        ? 0L
                        : witness.Minimum == witness.Maximum
                            ? 1L
                            : 2L,
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

        // WHY: Only missing and ambiguous origins need status markers. Successful origins already carry their
        // complete status in each evidence row, so splitting the branches adds no physical transport rows.
        var unresolvedRows = outcomes
            .Where(outcome => outcome.PersonId == null)
            .Select(s_singleGroupRowProjection);

        // WHY: Keep the independent evidence barrier that prevents MySQL from joining every membership of
        // a unique person to every origin before routing and path checks. Projecting the exact source/tree
        // keys preserves every original occurrence without admitting evidence from another origin.
        var admittedEvidence = candidates
            .Select(s_evidenceRowProjection)
            .Distinct();

        var uniquePeople = witnesses.Where(witness => witness.Minimum != null && witness.Minimum == witness.Maximum);

        // WHY: An independent inner join admits complete occurrences after server-side person reduction.
        // Null-extending all-origin evidence from each outcome put the full candidate relation underneath
        // every origin on SQL Server. Missing/ambiguous markers remain in the separate branch above.
        var resolvedEvidence = admittedEvidence.Join(
            uniquePeople,
            candidate => new
            {
                candidate.StartGroupId,
                PersonId = (int?)candidate.CandidateUserId,
            },
            witness => new
            {
                witness.StartGroupId,
                PersonId = witness.Minimum,
            },
            (candidate, witness) => candidate);

        return sourceRows
            .Concat(unresolvedRows)
            .Concat(resolvedEvidence);
    }

    /// <summary>Collects only requested fields for one explicit physical owner catalog.</summary>
    private static string[] Fields(
        IReadOnlyList<MetadataMatch> matches,
        MetadataOwner source,
        MetadataOwner candidate
    ) => matches
        .SelectMany(match => new[]
        {
            match.SourceOwner == source ? match.SourceFieldCode : null,
            match.CandidateOwner == candidate ? match.CandidateFieldCode : null,
        })
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .Select(SupervisorExactText.Suffix)
        .ToArray();
}
