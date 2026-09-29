using System.Data;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Shows a caller decision rolling back provisional hierarchy and role writes together.</summary>
internal static class RollbackScenario
{
    private static readonly Guid s_tenantId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000004");
    private static readonly Guid s_treeId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000104");

    /// <summary>Rolls back provisional access and checks that hierarchy and ordinary writes are unchanged.</summary>
    /// <param name="configuration">The sample-owned database configuration.</param>
    /// <param name="details">Whether tree output includes structural columns.</param>
    /// <param name="cancellationToken">The token used throughout setup, rollback, and verification.</param>
    /// <returns>A task that completes after the fresh persisted-state comparison succeeds.</returns>
    public static async Task RunAsync(
        SampleDatabaseConfiguration configuration,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(
            "ROLLBACK: denied approval removes provisional groups and grants together",
            cancellationToken);

        GroupHierarchy hierarchy;
        GroupStructure[] before;

        await using (var setup = UserGroupContexts.Create(configuration))
        {
            hierarchy = await GroupScenarioData.CreateHierarchyAsync(setup, s_tenantId, s_treeId, cancellationToken);
            before = await GroupScenarioData.ReadStructureAsync(setup, s_tenantId, s_treeId, cancellationToken);
            await GroupOutput.TreeAsync(
                setup,
                s_tenantId,
                s_treeId,
                "Before provisional access:",
                details,
                cancellationToken);
        }

        var provisionalGroup = new UserGroup { Name = "Provisional administrators" };
        Guid provisionalRoleId;

        await using (var context = UserGroupContexts.Create(configuration))
        {
            // WHY: The library joins this caller-owned transaction; only the application decides approval and commit.
            var isolation = configuration.Provider == SampleProvider.Sqlite
                ? IsolationLevel.Serializable
                : IsolationLevel.ReadCommitted;

            await using var transaction = await context.Database.BeginTransactionAsync(isolation, cancellationToken);
            await context
                .NestedSet<UserGroup>()
                .ForScope(s_tenantId)
                .InsertChildAsync(provisionalGroup, hierarchy.Finance.Id, cancellationToken);

            var provisionalRole = await GroupScenarioData.AssignRoleAsync(
                context,
                s_tenantId,
                provisionalGroup.Id,
                "Provisional administrator",
                "admin.provisional",
                cancellationToken);

            provisionalRoleId = provisionalRole.Id;
            await GroupOutput.TreeAsync(
                context,
                s_tenantId,
                s_treeId,
                "Visible inside the uncommitted transaction:",
                details,
                cancellationToken);

            await transaction.RollbackAsync(cancellationToken);
        }

        // WHY: Caller rollback does not rewind CLR objects or tracking; dispose this context and read afresh.
        await using var verification = UserGroupContexts.Create(configuration);
        var after = await GroupScenarioData.ReadStructureAsync(verification, s_tenantId, s_treeId, cancellationToken);

        SampleConsole.Require(
            before.SequenceEqual(after),
            "Caller rollback did not preserve the complete preexisting group structure.");

        SampleConsole.Require(
            !await verification.Groups.AnyAsync(
                group => group.TenantId == s_tenantId && group.Name == provisionalGroup.Name,
                cancellationToken),
            "A provisional group survived caller rollback.");

        SampleConsole.Require(
            !await verification.Roles.AnyAsync(
                role => role.TenantId == s_tenantId && role.Id == provisionalRoleId,
                cancellationToken),
            "A provisional role survived caller rollback.");

        SampleConsole.Require(
            !await verification.Privileges.AnyAsync(privilege => privilege.TenantId == s_tenantId, cancellationToken),
            "A provisional permission definition survived caller rollback.");

        SampleConsole.Require(
            !await verification.GroupRoles.AnyAsync(assignment => assignment.TenantId == s_tenantId, cancellationToken),
            "A provisional group-role assignment survived caller rollback.");

        SampleConsole.Require(
            !await verification.RolePrivileges.AnyAsync(grant => grant.TenantId == s_tenantId, cancellationToken),
            "A provisional role grant survived caller rollback.");

        await GroupOutput.TreeAsync(
            verification,
            s_tenantId,
            s_treeId,
            "After caller rollback:",
            details,
            cancellationToken);

        await GroupScenarioData.ValidateAsync(verification, s_tenantId, s_treeId, cancellationToken);
        await SampleConsole.WriteLineAsync(
            "All original coordinates and parents remain; no provisional group, role or grant remains.",
            cancellationToken);
    }
}
