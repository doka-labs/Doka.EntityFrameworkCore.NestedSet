namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Provisions a group and its ordinary role data in one caller-owned transaction.</summary>
internal static class CommitScenario
{
    private static readonly Guid s_tenantId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000003");
    private static readonly Guid s_treeId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000103");

    /// <summary>Commits the hierarchy and grant rows atomically, then checks them with a fresh context.</summary>
    /// <param name="configuration">The sample-owned database configuration.</param>
    /// <param name="details">Whether tree output includes structural columns.</param>
    /// <param name="cancellationToken">The token used throughout the transaction and verification.</param>
    /// <returns>A task that completes after the committed provisioning result has been verified.</returns>
    public static async Task RunAsync(
        SampleDatabaseConfiguration configuration,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(
            "COMMIT: provision a group and its roles as one application operation",
            cancellationToken);

        var organization = new UserGroup { Name = "Committed organization" };
        var team = new UserGroup { Name = "Delivery team" };
        Guid roleId;

        await using (var context = UserGroupContexts.Create(configuration))
        {
            // WHY: SQLite serializes writers; servers use the library's ReadCommitted caller-transaction contract.
            var isolation = configuration.Provider == SampleProvider.Sqlite
                ? IsolationLevel.Serializable
                : IsolationLevel.ReadCommitted;

            await using var transaction = await context.Database.BeginTransactionAsync(isolation, cancellationToken);
            var groups = context
                .NestedSet<UserGroup>()
                .ForScope(s_tenantId);

            await groups.InsertRootAsync(organization, s_treeId, cancellationToken);
            await groups.InsertChildAsync(team, organization.Id, cancellationToken);

            var role = await GroupScenarioData.AssignRoleAsync(
                context,
                s_tenantId,
                team.Id,
                "Delivery engineer",
                "app.deploy",
                cancellationToken);

            roleId = role.Id;
            await transaction.CommitAsync(cancellationToken);
        }

        // WHY: A separate unit of work verifies committed rows instead of trusting the transaction's tracked entities.
        await using var verification = UserGroupContexts.Create(configuration);
        await GroupOutput.TreeAsync(
            verification,
            s_tenantId,
            s_treeId,
            "Committed hierarchy:",
            details,
            cancellationToken);

        var permissions = await GroupOutput.PrivilegesAsync(
            verification,
            s_tenantId,
            team.Id,
            "Delivery team effective privileges",
            cancellationToken);

        SampleConsole.Require(
            permissions.SequenceEqual(["app.deploy"]),
            "Committed provisioning did not persist the group's grant.");

        var persistedRole = await verification
            .Roles
            .AsNoTracking()
            .SingleAsync(role => role.TenantId == s_tenantId && role.Id == roleId, cancellationToken);

        SampleConsole.Require(
            persistedRole.UpdatedAtUtc != default,
            "The committed role did not run the existing application save policy.");

        await GroupScenarioData.ValidateAsync(verification, s_tenantId, s_treeId, cancellationToken);
        await SampleConsole.WriteLineAsync(
            "Committed together: tree, group, role, permission definition, assignment and grant.",
            cancellationToken);
    }
}
