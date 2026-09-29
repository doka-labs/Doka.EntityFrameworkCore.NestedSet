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
        // WHY: Bounds are meaningful only inside the same tree and tenant; each assigned group supplies both identities.
        /* Query syntax alternative:
        var applicableGroups = from membership in context.UserGroupMemberships
            join assigned in context.Groups on new
            {
                membership.TenantId,
                Id = membership.UserGroupId,
            } equals new
            {
                assigned.TenantId,
                assigned.Id,
            }
            join ancestor in context.Groups on new
            {
                assigned.TenantId,
                assigned.TreeId,
            } equals new
            {
                ancestor.TenantId,
                ancestor.TreeId,
            }
            where membership.TenantId == tenantId
                && membership.UserId == userId
                && ancestor.Left <= assigned.Left
                && ancestor.Right >= assigned.Right
            select ancestor.Id;
        */

        var applicableGroups = context
            .UserGroupMemberships
            .Join(
                context.Groups,
                membership => new { membership.TenantId, Id = membership.UserGroupId },
                assigned => new { assigned.TenantId, assigned.Id },
                (membership, assigned) => new { membership, assigned })
            .Join(
                context.Groups,
                pair => new { pair.assigned.TenantId, pair.assigned.TreeId },
                ancestor => new { ancestor.TenantId, ancestor.TreeId },
                (pair, ancestor) => new { pair.membership, pair.assigned, ancestor })
            .Where(row => row.membership.TenantId == tenantId
                && row.membership.UserId == userId
                && row.ancestor.Left <= row.assigned.Left
                && row.ancestor.Right >= row.assigned.Right)
            .Select(row => row.ancestor.Id);

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
