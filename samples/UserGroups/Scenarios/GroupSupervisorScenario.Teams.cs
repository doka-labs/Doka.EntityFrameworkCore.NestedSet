namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class GroupSupervisorScenario
{
    /// <summary>Runs the independently selected team-supervisors demonstration.</summary>
    private static async Task RunTeamAsync(
        UserGroupContext context,
        SupervisorScenarioFixture fixture,
        SupervisorRequest request,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        SearchStart[] starts = [SearchStart.IncludeSelf, SearchStart.ParentsOnly];

        foreach (var start in starts)
        {
            SupervisorRoles[] roleSources =
            [
                new MembershipRoleFilter(fixture.ManagerRoleId),
                new GlobalUserRole(fixture.ManagerRoleId),
                new EitherRole(fixture.ManagerRoleId),
            ];

            foreach (var roles in roleSources)
            {
                var result = await SupervisorQueries.ResolveAsync(
                    context,
                    fixture.TenantId,
                    request with
                    {
                        StartGroupId = null,
                        SearchStart = start,
                        Roles = roles,
                    },
                    SupervisorSelection.All,
                    cancellationToken);

                await PrintAsync(
                    $"{start}; {roles.GetType().Name}; All",
                    result,
                    fixture,
                    details,
                    output,
                    cancellationToken);
            }
        }

        var unique = await SupervisorQueries.ResolveAsync(
            context,
            fixture.TenantId,
            request,
            SupervisorSelection.RequireUnique,
            cancellationToken);

        var priority = await SupervisorQueries.ResolveAsync(
            context,
            fixture.TenantId,
            request,
            SupervisorSelection.HighestBusinessPriority,
            cancellationToken);

        await PrintAsync(
            "RequireUnique (Anna and Alex remain ambiguous)",
            unique,
            fixture,
            details,
            output,
            cancellationToken);

        await PrintAsync(
            "HighestBusinessPriority (Anna=10; Alex=20)",
            priority,
            fixture,
            details,
            output,
            cancellationToken);
    }
}
