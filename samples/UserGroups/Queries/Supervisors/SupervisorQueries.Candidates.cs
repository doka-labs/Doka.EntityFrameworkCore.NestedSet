namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Composes application role and routing rules over generic nested-set queries.</summary>
internal static partial class SupervisorQueries
{
    /// <summary>Composes candidates over the caller's already validated, exact visible origin relation.</summary>
    private static IQueryable<SupervisorCandidate> BuildCandidates(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request,
        IQueryable<SupervisorOrigin> origins,
        IQueryable<UserGroupMembership> memberships
    )
    {
        var hierarchy = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        // WHY: An explicit group and the membership's (tenant, user, group) primary key prove at most one
        // origin. Only that case retains the inclusive relation's selective endpoint pushdown. Unknown
        // origin cardinality uses strict ranges plus exact self rows to avoid a source-by-group range join.
        var singleOriginInclusive = request is { Groups: AllEligibleGroups, StartGroupId: not null };

        // WHY: Strict source keys need only a membership witness. Embedding user and group authorization
        // there correlates two hierarchy roots and prevents selective indexed ancestor composition.
        // The exact validated origin relation is joined back before any rank, routing or path decision.
        var originKeys = singleOriginInclusive
            ? origins.Select(s_originGroupKey)
            : memberships.Select(s_membershipGroupKey);

        // WHY: These nonnullable keys expose a source-key semijoin. A correlated membership EXISTS
        // let SQLite enumerate ancestor/start pairs across a deep tree before selecting the actual origin.
        var paths = singleOriginInclusive
            ? hierarchy.SelectAncestorsOrSelf(group => originKeys.Contains(group.Id), s_groupPathProjection)
            : hierarchy.SelectAncestors(group => originKeys.Contains(group.Id), s_groupPathProjection);

        if (singleOriginInclusive)
        {
            if (request.SearchStart == SearchStart.ParentsOnly)
            {
                paths = paths.Where(path => path.Distance > 0);
            }
        }
        else
        {
            // WHY: Membership keys are necessary witnesses, never authorization. Rejoin the visible source
            // and group relation before group windows, including hidden-user and hidden-group checks.
            // The fixed source user and membership primary key prove one origin per exact tenant/group.
            paths = paths.Join(
                origins,
                path => new
                {
                    path.StartGroup.TenantId,
                    path.StartGroup.Id,
                },
                origin => new
                {
                    origin.Group.TenantId,
                    origin.Group.Id,
                },
                (path, origin) => path);

            if (request.SearchStart == SearchStart.IncludeSelf)
            {
                // WHY: Each origin needs one exact self slot beside strict ancestor ranges. The disjoint branches
                // share this statement's snapshot and preserve every authorized source occurrence.
                paths = paths.Concat(
                    origins.Select(origin => new SupervisorGroupPath
                    {
                        StartGroup = origin.Group,
                        FoundGroup = origin.Group,
                        Distance = 0,
                    }));
            }
        }

        var types = request.Groups switch
        {
            AllEligibleGroups all => all.Types,
            NthEligibleGroup nth => nth.Types,
            NthTypeGroupThenValidate typeRule => [typeRule.TypeId],
            _ => throw new ArgumentException("Unknown group selection.", nameof(request)),
        };

        if (types.Length != 0)
        {
            paths = paths.Where(row => row.FoundGroup.TypeId != null
                && Enumerable.Contains(EF.Parameter(types), row.FoundGroup.TypeId.Value));
        }

        if (request.Groups is NthTypeGroupThenValidate typed)
        {
            var typedPaths = paths;
            // WHY: A type winner precedes membership, routing and edge validation, so rejection cannot admit
            // a farther group. Structural pairs are unique per origin/group; retain their mapped endpoints in
            // the partitioned selection instead of copying the whole path query to rejoin selected keys.
            paths = origins.SelectMany(
                origin => typedPaths
                    .Where(path => path.StartGroup.Id == origin.Group.Id)
                    .OrderBy(path => path.Distance)
                    .Skip(typed.Number - 1)
                    .Take(1),
                (origin, path) => path);
        }

        var onDay = request.OnDay;
        var members = EligibleMemberships(context, tenantId, request.SourceUserId, request.Roles, onDay);
        var sourceUser = request.SourceUserId;
        var pairs = MembershipPairs(paths, members, tenantId, sourceUser);

        pairs = ApplyRouting(context, tenantId, request, pairs);

        // WHY: Select a type winner before validating its path, but prune concrete role/membership/routing endpoints
        // before complete-edge checks. Deep trees must not validate every ancestor before rejecting its people.
        // Keep each period proof scalar: flattening EXISTS joined repeated parent periods to every child
        // before matching the exact child. Every match projects one, including overlapping periods; an empty
        // relation returns zero. The witness preserves existence without selecting a period or reserving a key.
        pairs = hierarchy.WherePathEdges(
            pairs,
            row => row.FoundGroup,
            row => row.StartGroup,
            (row, parent, child) => onDay == null
                || context
                    .GroupRelationPeriods
                    .Where(period =>
                        period.TenantId == tenantId
                        && period.ParentId == parent.Id
                        && period.ChildId == child.Id
                        && period.FromDate <= onDay
                        && (period.ToDate == null || period.ToDate >= onDay))
                    .Select(period => 1L)
                    .FirstOrDefault()
                == 1L);

        var rows = pairs.Select(s_candidateProjection);

        if (request.Groups is NthEligibleGroup eligible)
        {
            var candidates = rows;
            var selected = SelectGroupKeys(candidates.Select(s_groupPositionProjection), origins, eligible.Number);

            var groups = context.Groups.Where(group => group.TenantId == tenantId);
            var selectedPaths = selected
                .Join(
                    groups,
                    choice => choice.StartGroupId,
                    group => group.Id,
                    (choice, start) => new
                    {
                        Choice = choice,
                        Start = start,
                    })
                .Join(
                    groups,
                    row => row.Choice.FoundGroupId,
                    group => group.Id,
                    (
                        row,
                        found
                    ) => new SupervisorGroupPath
                    {
                        StartGroup = row.Start,
                        FoundGroup = found,
                        Distance = row.Choice.Distance,
                    });

            // WHY: The group window already proved the entire path, whose rule depends only on its endpoints
            // and date. Rehydrate those exact mapped keys, then qualify every person again; rebuilding the
            // original range here made PostgreSQL compare each selected group against the entire path bag.
            var selectedPairs = MembershipPairs(selectedPaths, members, tenantId, sourceUser);
            rows = ApplyRouting(context, tenantId, request, selectedPairs)
                .Select(s_candidateProjection);
        }

        return rows;
    }

    /// <summary>Projects every eligible exact membership on the supplied mapped group paths.</summary>
    private static IQueryable<SupervisorCandidatePath> MembershipPairs(
        IQueryable<SupervisorGroupPath> paths,
        IQueryable<UserGroupMembership> members,
        Guid tenantId,
        int sourceUser
    ) => paths.Join(
        members,
        path => path.FoundGroup.Id,
        member => member.UserGroupId,
        (path, member) => new SupervisorCandidatePath
        {
            StartGroup = path.StartGroup,
            FoundGroup = path.FoundGroup,
            Candidate = new SupervisorCandidate
            {
                TenantId = tenantId,
                SourceUserId = sourceUser,
                StartGroupId = path.StartGroup.Id,
                StartTreeId = path.StartGroup.TreeId,
                FoundGroupId = path.FoundGroup.Id,
                CandidateUserId = member.UserId,
                Distance = path.Distance,
                LeadershipPriority = member.LeadershipPriority,
            },
        });

    /// <summary>Selects one qualified group ordinal per visible origin.</summary>
    private static IQueryable<SupervisorGroupChoice> SelectGroupKeys(
        IQueryable<SupervisorGroupPosition> groups,
        IQueryable<SupervisorOrigin> origins,
        int number
    )
    {
        // WHY: Reduce multiple people in one group to one ordinal slot. OFFSET 0 leaves that relation
        // independent when EF extracts the later origin equality. Ordinary SelectMany with Skip/Take can then
        // become one partitioned ROW_NUMBER stage instead of rescanning the hierarchy once per origin.
        var positions = groups
            .GroupBy(group => new
            {
                group.StartGroupId,
                group.FoundGroupId
            })
            .Select(group => new SupervisorGroupPosition
            {
                StartGroupId = group.Key.StartGroupId,
                FoundGroupId = group.Key.FoundGroupId,
                Distance = group.Min(row => row.Distance),
            })
            .OrderBy(group => group.StartGroupId)
            .ThenBy(group => group.FoundGroupId)
            .Skip(0);

        // WHY: A missing ordinal contributes no selected key. Resolve retains its outer origin row and explicit
        // NotFound status; nullable key equality never reserves otherwise valid zero or negative group IDs.
        return origins.SelectMany(
            origin => positions
                .Where(group => group.StartGroupId == origin.Group.Id)
                .OrderBy(group => group.Distance)
                .Skip(number - 1)
                .Take(1),
            (origin, group) => new SupervisorGroupChoice
            {
                StartGroupId = origin.Group.Id,
                FoundGroupId = group.FoundGroupId,
                Distance = group.Distance,
            });
    }

    /// <summary>Selects necessary own membership keys without asserting source-user or group visibility.</summary>
    private static IQueryable<UserGroupMembership> SourceMemberships(
        UserGroupContext context,
        Guid tenantId,
        SupervisorRequest request
    )
    {
        var source = request.SourceUserId;
        var start = request.StartGroupId;
        var day = request.OnDay;

        return context.UserGroupMemberships.Where(member => member.TenantId == tenantId
            && member.UserId == source
            && (start == null || member.UserGroupId == start)
            && (day == null
                || ((member.FromDate == null || member.FromDate <= day)
                    && (member.ToDate == null || member.ToDate >= day))));
    }

    /// <summary>Authorizes the exact visible source user and group over the shared membership relation.</summary>
    private static IQueryable<SupervisorOrigin> Origins(
        UserGroupContext context,
        Guid tenantId,
        int source,
        IQueryable<UserGroupMembership> memberships
    )
    {
        var visibleMemberships = memberships.Where(member => context.Users.Any(user =>
            user.TenantId == tenantId && user.Id == source));

        return visibleMemberships.Join(
            context.Groups.Where(group => group.TenantId == tenantId),
            s_membershipGroupKey,
            s_groupKey,
            s_originProjection);
    }

    /// <summary>Prunes concrete membership endpoints before joining them to structural paths.</summary>
    private static IQueryable<UserGroupMembership> EligibleMemberships(
        UserGroupContext context,
        Guid tenantId,
        int sourceUser,
        SupervisorRoles roles,
        DateOnly? day
    )
    {
        var rows = context.UserGroupMemberships.Where(member => member.TenantId == tenantId
            && member.UserId != sourceUser
            && context.Users.Any(user => user.TenantId == tenantId && user.Id == member.UserId)
            && (day == null
                || ((member.FromDate == null || member.FromDate <= day)
                    && (member.ToDate == null || member.ToDate >= day))));

        if (roles is NoRoleFilter)
        {
            return rows;
        }

        var roleId = roles switch
        {
            MembershipRoleFilter local => local.RoleId,
            GlobalUserRole global => global.RoleId,
            EitherRole either => either.RoleId,
            _ => throw new ArgumentException("Unknown person-role source.", nameof(roles)),
        };

        var localAllowed = roles is MembershipRoleFilter or EitherRole;
        var globalAllowed = roles is GlobalUserRole or EitherRole;

        // WHY: EXISTS preserves one membership with both grant sources. Keep the complete check inside a
        // numeric condition so a flattened grant bag cannot multiply every selected group by all role holders.
        return rows.Where(member => (context.Roles.Any(role => role.TenantId == tenantId && role.Id == roleId)
                && ((localAllowed
                        && context.MembershipRoles.Any(grant => grant.TenantId == tenantId
                            && grant.RoleId == roleId
                            && grant.UserId == member.UserId
                            && grant.UserGroupId == member.UserGroupId
                            && (day == null
                                || ((grant.FromDate == null || grant.FromDate <= day)
                                    && (grant.ToDate == null || grant.ToDate >= day)))))
                    || (globalAllowed
                        && context.UserRoles.Any(grant => grant.TenantId == tenantId
                            && grant.RoleId == roleId
                            && grant.UserId == member.UserId
                            && (day == null
                                || ((grant.FromDate == null || grant.FromDate <= day)
                                    && (grant.ToDate == null || grant.ToDate >= day))))))
                    ? 1L
                    : 0L)
            != 0L);
    }

    /// <summary>
    /// Reuses one deferred owner projection across same-owner criteria instead of rebuilding its catalog predicates.
    /// </summary>
    private static IQueryable<SupervisorRoutingValue> SharedRoutingValues(
        UserGroupContext context,
        Guid tenantId,
        MetadataOwner owner,
        IQueryable<SupervisorRoutingValue>?[] sources
    )
    {
        var index = owner switch
        {
            MetadataOwner.SourceUser or MetadataOwner.CandidateUser => 0,
            MetadataOwner.SourceMembership or MetadataOwner.CandidateMembership => 1,
            MetadataOwner.SourceGroup or MetadataOwner.CandidateGroup => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };

        return sources[index] ??= RoutingValues(context, owner)
            .Where(value => value.TenantId == tenantId);
    }

    /// <summary>Normalizes only the three explicitly chosen owner sources into scalar query projections.</summary>
    private static IQueryable<SupervisorRoutingValue> RoutingValues(
        UserGroupContext context,
        MetadataOwner owner
    )
    {
        // WHY: Owner-specific tables keep physical FKs; this shape sharing stays entirely in server expressions.
        var values = owner switch
        {
            MetadataOwner.SourceUser or MetadataOwner.CandidateUser => context
                .UserMetadatas
                .Where(value => context.UserMetadataDefinitions.Any(definition => definition.TenantId == value.TenantId
                    && SupervisorExactText.FieldEquals(definition.FieldCode, value.FieldCode)
                    && SupervisorExactText.FieldEquals(definition.ValueKind, "Text")))
                .Select(value => new SupervisorRoutingValue
                {
                    TenantId = value.TenantId,
                    UserId = value.UserId,
                    GroupId = 0,
                    FieldCode = value.FieldCode,
                    Value = value.Value,
                }),
            MetadataOwner.SourceMembership or MetadataOwner.CandidateMembership => context
                .MembershipMetadatas
                .Where(value =>
                    context.Groups.Any(group =>
                        group.TenantId == value.TenantId
                        && group.Id == value.UserGroupId
                        && group.TypeId == value.GroupTypeId)
                    && context.MembershipMetadataDefinitions.Any(definition => definition.TenantId == value.TenantId
                        && definition.GroupTypeId == value.GroupTypeId
                        && SupervisorExactText.FieldEquals(definition.FieldCode, value.FieldCode)
                        && SupervisorExactText.FieldEquals(definition.ValueKind, "Text")))
                .Select(value => new SupervisorRoutingValue
                {
                    TenantId = value.TenantId,
                    UserId = value.UserId,
                    GroupId = value.UserGroupId,
                    FieldCode = value.FieldCode,
                    Value = value.Value,
                }),
            MetadataOwner.SourceGroup or MetadataOwner.CandidateGroup => context
                .GroupMetadatas
                .Where(value =>
                    context.Groups.Any(group =>
                        group.TenantId == value.TenantId
                        && group.Id == value.UserGroupId
                        && group.TypeId == value.GroupTypeId)
                    && context.GroupMetadataDefinitions.Any(definition => definition.TenantId == value.TenantId
                        && definition.GroupTypeId == value.GroupTypeId
                        && SupervisorExactText.FieldEquals(definition.FieldCode, value.FieldCode)
                        && SupervisorExactText.FieldEquals(definition.ValueKind, "Text")))
                .Select(value => new SupervisorRoutingValue
                {
                    TenantId = value.TenantId,
                    UserId = 0,
                    GroupId = value.UserGroupId,
                    FieldCode = value.FieldCode,
                    Value = value.Value,
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };

        return values.Provider.CreateQuery<SupervisorRoutingValue>(SupervisorExactText.Inline(values.Expression));
    }
}
