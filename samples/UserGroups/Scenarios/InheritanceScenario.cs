namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Shows how a group keeps its own grants while a move changes its inherited grants.</summary>
internal static class InheritanceScenario
{
    private static readonly Guid s_tenantId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000001");
    private static readonly Guid s_treeId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000101");
    private static readonly Guid s_otherTenantId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000002");

    /// <summary>Creates independent tenant data, changes the hierarchy, and verifies the SQL grant result.</summary>
    /// <param name="configuration">The database owned by this sample.</param>
    /// <param name="details">Whether tree output includes structural columns.</param>
    /// <param name="cancellationToken">The token used throughout the scenario.</param>
    /// <returns>A task that completes after the inherited and direct grants have been verified.</returns>
    public static async Task RunAsync(
        SampleDatabaseConfiguration configuration,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(
            "INHERITANCE: own grants stay; ancestor grants follow the new branch",
            cancellationToken);

        GroupHierarchy hierarchy;
        var foreignRoot = new UserGroup { Name = "Other tenant organization" };

        await using (var setup = UserGroupContexts.Create(configuration))
        {
            hierarchy = await GroupScenarioData.CreateHierarchyAsync(setup, s_tenantId, s_treeId, cancellationToken);
            await GroupScenarioData.AssignRoleAsync(
                setup,
                s_tenantId,
                hierarchy.Organization.Id,
                "Member",
                "directory.read",
                cancellationToken);

            await GroupScenarioData.AssignRoleAsync(
                setup,
                s_tenantId,
                hierarchy.Engineering.Id,
                "Engineer",
                "app.deploy",
                cancellationToken);

            await GroupScenarioData.AssignRoleAsync(
                setup,
                s_tenantId,
                hierarchy.Platform.Id,
                "Platform maintainer",
                "platform.configure",
                cancellationToken);

            await GroupScenarioData.AssignRoleAsync(
                setup,
                s_tenantId,
                hierarchy.ApiTeam.Id,
                "Auditor",
                "logs.read",
                cancellationToken);

            await GroupScenarioData.AssignRoleAsync(
                setup,
                s_tenantId,
                hierarchy.Finance.Id,
                "Billing approver",
                "invoices.approve",
                cancellationToken);

            // WHY: Equal TreeId values in separate scopes are legal; the unrelated administrator must remain isolated.
            await setup
                .NestedSet<UserGroup>()
                .ForScope(s_otherTenantId)
                .InsertRootAsync(foreignRoot, s_treeId, cancellationToken);

            await GroupScenarioData.AssignRoleAsync(
                setup,
                s_otherTenantId,
                foreignRoot.Id,
                "Administrator",
                "admin.root",
                cancellationToken);
        }

        await using var context = UserGroupContexts.Create(configuration);
        await GroupOutput.TreeAsync(context, s_tenantId, s_treeId, "Before move:", details, cancellationToken);

        var before = await GroupOutput.PrivilegesAsync(
            context,
            s_tenantId,
            hierarchy.ApiTeam.Id,
            "API team effective privileges",
            cancellationToken);

        SampleConsole.Require(
            before.SequenceEqual(["app.deploy", "directory.read", "logs.read", "platform.configure"]),
            "The API team did not receive exactly its own and its scoped ancestor grants before the move.");

        await context
            .NestedSet<UserGroup>()
            .ForScope(s_tenantId)
            .MoveToAsync(hierarchy.ApiTeam.Id, hierarchy.Finance.Id, cancellationToken);

        await GroupOutput.TreeAsync(
            context,
            s_tenantId,
            s_treeId,
            "After moving API team under Finance:",
            details,
            cancellationToken);

        var after = await GroupOutput.PrivilegesAsync(
            context,
            s_tenantId,
            hierarchy.ApiTeam.Id,
            "API team effective privileges",
            cancellationToken);

        SampleConsole.Require(
            after.SequenceEqual(["directory.read", "invoices.approve", "logs.read"]),
            "The API team's own logs.read grant or its new inherited Finance grants are incorrect.");

        var foreignLookup = await PrivilegeQueries.EffectiveAsync(
            context,
            s_tenantId,
            foreignRoot.Id,
            cancellationToken);

        SampleConsole.Require(
            foreignLookup.Count == 0,
            "A foreign-tenant anchor leaked grants into the selected tenant.");

        var movedGroup = await context.Groups.SingleAsync(
            group => group.TenantId == s_tenantId && group.Id == hierarchy.ApiTeam.Id,
            cancellationToken);

        var originalTimestamp = movedGroup.UpdatedAtUtc;
        movedGroup.Name = "API maintainers";
        await context.SaveChangesAsync(cancellationToken);

        var persistedGroup = await context
            .Groups
            .AsNoTracking()
            .SingleAsync(group => group.TenantId == s_tenantId && group.Id == hierarchy.ApiTeam.Id, cancellationToken);

        SampleConsole.Require(
            persistedGroup.Name == "API maintainers",
            "The normal tracked payload rename was not saved.");

        SampleConsole.Require(
            persistedGroup.UpdatedAtUtc != originalTimestamp,
            "The existing SaveChangesAsync timestamp customization did not update the persisted payload.");

        await GroupScenarioData.ValidateAsync(context, s_tenantId, s_treeId, cancellationToken);
        await GroupScenarioData.ValidateAsync(context, s_otherTenantId, s_treeId, cancellationToken);
        await SampleConsole.WriteLineAsync(
            "Own logs.read remains; old branch grants disappear; admin.root stays in the other tenant.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            "Normal tracked payload save also ran the application's UTC timestamp customization.",
            cancellationToken);
    }
}
