namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Creates small, explicitly named scenario data through public hierarchy and EF APIs.</summary>
internal static class GroupScenarioData
{
    /// <summary>Creates the same recognizable shape inside the caller's independently chosen tenant and tree.</summary>
    /// <param name="context">The clean caller-owned context.</param>
    /// <param name="tenantId">The tenant partition assigned to this scenario.</param>
    /// <param name="treeId">The never-before-used tree identity.</param>
    /// <param name="cancellationToken">The token used for every insert.</param>
    /// <returns>The named nodes with their persisted keys.</returns>
    public static async Task<GroupHierarchy> CreateHierarchyAsync(
        UserGroupContext context,
        Guid tenantId,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var groups = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        var organization = new UserGroup { Name = "Organization" };
        var engineering = new UserGroup { Name = "Engineering" };
        var platform = new UserGroup { Name = "Platform" };
        var apiTeam = new UserGroup { Name = "API team" };
        var finance = new UserGroup { Name = "Finance" };

        await groups.InsertRootAsync(organization, treeId, cancellationToken);
        await groups.InsertChildAsync(engineering, organization.Id, cancellationToken);
        await groups.InsertChildAsync(platform, engineering.Id, cancellationToken);
        await groups.InsertChildAsync(apiTeam, platform.Id, cancellationToken);
        await groups.InsertChildAsync(finance, organization.Id, cancellationToken);

        return new GroupHierarchy(
            tenantId,
            treeId,
            organization,
            engineering,
            platform,
            apiTeam,
            finance);
    }

    /// <summary>Saves a clearly associated role, permission definition, assignment, and grant.</summary>
    /// <param name="context">The context whose save boundary is also used by a caller transaction when present.</param>
    /// <param name="tenantId">The tenant owning every relationship endpoint.</param>
    /// <param name="groupId">The persisted group receiving the role.</param>
    /// <param name="roleName">The human-readable role name.</param>
    /// <param name="privilegeCode">A permission code not yet defined in this scenario's tenant.</param>
    /// <param name="cancellationToken">The token used for adding and saving the domain entities.</param>
    /// <returns>The saved role for precise commit and rollback checks.</returns>
    public static async Task<Role> AssignRoleAsync(
        UserGroupContext context,
        Guid tenantId,
        int groupId,
        string roleName,
        string privilegeCode,
        CancellationToken cancellationToken
    )
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = roleName,
        };

        await context.AddRangeAsync(
            [
                role,
                new Privilege
                {
                    TenantId = tenantId,
                    Code = privilegeCode,
                },
                new UserGroupRole
                {
                    TenantId = tenantId,
                    UserGroupId = groupId,
                    RoleId = role.Id,
                },
                new RolePrivilege
                {
                    TenantId = tenantId,
                    RoleId = role.Id,
                    PrivilegeCode = privilegeCode,
                },
            ],
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return role;
    }

    /// <summary>Reads a compact structural snapshot without adding tracker entries.</summary>
    /// <param name="context">The context used for the persisted-state query.</param>
    /// <param name="tenantId">The tenant containing the selected tree.</param>
    /// <param name="treeId">The tree whose structure is compared.</param>
    /// <param name="cancellationToken">The token used for the query.</param>
    /// <returns>Structural rows sorted by key for value comparisons.</returns>
    public static Task<GroupStructure[]> ReadStructureAsync(
        UserGroupContext context,
        Guid tenantId,
        Guid treeId,
        CancellationToken cancellationToken
    ) => context
        .NestedSet<UserGroup>()
        .ForScope(tenantId)
        .InTree(treeId)
        .Nodes
        .OrderBy(group => group.Id)
        .Select(group => new GroupStructure(
            group.Id,
            group.ParentId,
            group.Left,
            group.Right,
            group.Depth,
            group.Position))
        .ToArrayAsync(cancellationToken);

    /// <summary>Validates all persisted invariants after a scenario's expected result has been checked.</summary>
    /// <param name="context">The clean context used for validation.</param>
    /// <param name="tenantId">The tenant containing the selected tree.</param>
    /// <param name="treeId">The tree to validate.</param>
    /// <param name="cancellationToken">The token used for the validation queries.</param>
    /// <returns>A task that completes after full validation succeeds.</returns>
    public static async Task ValidateAsync(
        UserGroupContext context,
        Guid tenantId,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var report = await context
            .NestedSet<UserGroup>()
            .ForScope(tenantId)
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

        SampleConsole.Require(
            report.IsValid,
            "The group tree is invalid: " + string.Join("; ", report.Issues.Select(issue => issue.Message)));
    }
}
