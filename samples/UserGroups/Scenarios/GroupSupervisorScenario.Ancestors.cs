namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

internal static partial class GroupSupervisorScenario
{
    /// <summary>Runs the independently selected ancestor-selection demonstration.</summary>
    private static async Task RunAncestorSelectionAsync(
        UserGroupContext context,
        SupervisorScenarioFixture fixture,
        SupervisorRequest request,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await DemonstrateDirectUserAncestorsAsync(context, fixture, output, cancellationToken);
        var structural = await context
            .NestedSet<UserGroup>()
            .ForScope(fixture.TenantId)
            .SelectAncestors(
                group => group.Id == fixture.Groups[5],
                (source, ancestor, distance) => new
                {
                    ancestor.Name,
                    Distance = distance,
                })
            .Where(row => row.Distance == 2)
            .ToArrayAsync(cancellationToken);

        await SampleConsole.WriteLineAsync(
            output,
            "Structural distance 2: " + string.Join(", ", structural.Select(row => row.Name)),
            cancellationToken);

        var secondGroup = await SupervisorQueries.ResolveAsync(
            context,
            fixture.TenantId,
            request with { Groups = new NthEligibleGroup([], 2) },
            SupervisorSelection.RequireUnique,
            cancellationToken);

        var secondPerson = await SupervisorQueries.ResolveAsync(
            context,
            fixture.TenantId,
            request with { Groups = new AllEligibleGroups() },
            SupervisorSelection.NthPerson(2),
            cancellationToken);

        await PrintAsync("Second eligible group: Ben", secondGroup, fixture, details, output, cancellationToken);
        await PrintAsync("Second ranked person: Alex", secondPerson, fixture, details, output, cancellationToken);
    }

    /// <summary>Keeps direct user ancestors independent of the group-derived supervisor relation.</summary>
    private static async Task DemonstrateDirectUserAncestorsAsync(
        UserGroupContext context,
        SupervisorScenarioFixture data,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var users = context
            .NestedSet<User>()
            .ForScope(data.TenantId);

        await users.MoveToAsync(data.Users[0], data.Users[1], cancellationToken);
        await users.MoveToAsync(data.Users[1], data.Users[3], cancellationToken);
        await context.UserRoles.AddRangeAsync(
            [
                new UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    RoleId = data.ManagerRoleId,
                },
                new UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[3],
                    RoleId = data.ManagerRoleId,
                },
            ],
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        var ancestors = users.SelectAncestors(
            user => user.Id == data.Users[0],
            (source, ancestor, distance) => new
            {
                ancestor.Id,
                ancestor.Name,
                Distance = distance,
            });

        var exactSecond = await ancestors
            .Where(row => row.Distance == 2)
            .ToArrayAsync(cancellationToken);

        var secondRoleholder = await ancestors
            .Where(row => context.UserRoles.Any(role =>
                role.TenantId == data.TenantId && role.UserId == row.Id && role.RoleId == data.ManagerRoleId))
            .OrderBy(row => row.Distance)
            .Skip(1)
            .Take(1)
            .ToArrayAsync(cancellationToken);

        await SampleConsole.WriteLineAsync(
            output,
            "Direct User hierarchy: structural ancestor 2 = "
            + string.Join(", ", exactSecond.Select(row => row.Name))
            + "; matching roleholder 2 = "
            + string.Join(", ", secondRoleholder.Select(row => row.Name)),
            cancellationToken);
    }
}
