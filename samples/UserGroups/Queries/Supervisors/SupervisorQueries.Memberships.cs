namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class SupervisorQueries
{

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
}
