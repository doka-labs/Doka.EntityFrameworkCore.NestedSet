namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Expresses the application's additive grant policy as a composable SQL query.</summary>
internal static class PrivilegeQueries
{
    /// <summary>Queries the selected group and ancestor grants without first loading the anchor.</summary>
    /// <param name="context">The application context containing the ordinary role relationships.</param>
    /// <param name="tenantId">The tenant partition applied to hierarchy and domain queries.</param>
    /// <param name="groupId">The group receiving direct and inherited grants.</param>
    /// <param name="cancellationToken">The token used for the final database query.</param>
    /// <returns>Effective permission codes in database sort order; an absent anchor yields an empty list.</returns>
    public static Task<List<string>> EffectiveAsync(
        UserGroupContext context,
        Guid tenantId,
        int groupId,
        CancellationToken cancellationToken
    )
    {
        var groups = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        // WHY: Ancestors are strict; union the visible scoped anchor so its own roles survive a hierarchy move.
        var applicableGroups = groups
            .AncestorsOf(groupId)
            .Select(group => group.Id)
            .Union(
                groups
                    .TreeContaining(groupId)
                    .Where(group => group.Id == groupId)
                    .Select(group => group.Id));

        return CodesForGroups(context, tenantId, applicableGroups)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Combines direct and inherited grants from every application group assigned to a user.</summary>
    /// <param name="context">The application context containing users, groups, and grants.</param>
    /// <param name="tenantId">The tenant partition applied for both membership endpoints.</param>
    /// <param name="userId">The user whose effective grants are requested.</param>
    /// <param name="cancellationToken">The token used for the final database query.</param>
    /// <returns>Distinct permission codes in database sort order; a user without groups has none.</returns>
    public static Task<List<string>> EffectiveForUserAsync(
        UserGroupContext context,
        Guid tenantId,
        int userId,
        CancellationToken cancellationToken
    )
    {
        var groups = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        var memberships = context.UserGroupMemberships.Where(membership =>
            membership.TenantId == tenantId && membership.UserId == userId);

        // WHY: Each own membership anchors a separate tenant/tree path; generic composition owns structural
        // comparisons.
        /* Query syntax alternative:
        var applicableGroups = from groupId in groups.SelectAncestorsOrSelf(
            selected => memberships.Any(membership => membership.UserGroupId == selected.Id),
            (selected, ancestor, distance) => ancestor.Id)
            select groupId;
        */
        var applicableGroups = groups.SelectAncestorsOrSelf(
            selected => memberships.Any(membership => membership.UserGroupId == selected.Id),
            (
                selected,
                ancestor,
                distance
            ) => ancestor.Id);

        return CodesForGroups(context, tenantId, applicableGroups)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Projects the application's additive grants for a server-side set of applicable groups.</summary>
    private static IOrderedQueryable<string> CodesForGroups(
        UserGroupContext context,
        Guid tenantId,
        IQueryable<int> applicableGroups
    )
    {
        var applicableRoles = context
            .GroupRoles
            .Where(assignment => assignment.TenantId == tenantId && applicableGroups.Contains(assignment.UserGroupId))
            .Select(assignment => assignment.RoleId);

        // WHY: The application's policy is additive; Distinct removes overlap without adding deny or override rules.
        return context
            .RolePrivileges
            .Where(grant => grant.TenantId == tenantId && applicableRoles.Contains(grant.RoleId))
            .Select(grant => grant.PrivilegeCode)
            .Distinct()
            .OrderBy(code => code);
    }
}
