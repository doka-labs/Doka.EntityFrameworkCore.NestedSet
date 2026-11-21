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
        // WHY: Keep EF-bound date/type/routing captures in one construction frame. Extracted joins capture
        // no state; separate parameterized closure frames would retain additional query construction objects.
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
            paths = AuthorizeStrictPaths(paths, origins, request.SearchStart == SearchStart.IncludeSelf);
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
            var selectedPaths = SelectedGroupPaths(selected, groups);

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
                group.FoundGroupId,
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

    /// <summary>
    /// Rejoins strict membership witnesses to their authorized visible origins without captured state.
    /// </summary>
    private static IQueryable<SupervisorGroupPath> AuthorizeStrictPaths(
        IQueryable<SupervisorGroupPath> paths,
        IQueryable<SupervisorOrigin> origins,
        bool includeSelf
    )
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

        if (includeSelf)
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

        return paths;
    }

    /// <summary>Rehydrates only the exact already-qualified endpoint keys without copying the path relation.</summary>
    private static IQueryable<SupervisorGroupPath> SelectedGroupPaths(
        IQueryable<SupervisorGroupChoice> selected,
        IQueryable<UserGroup> groups
    ) => selected
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
            (row,
                found) => new SupervisorGroupPath
            {
                StartGroup = row.Start,
                FoundGroup = found,
                Distance = row.Choice.Distance,
            });

}
