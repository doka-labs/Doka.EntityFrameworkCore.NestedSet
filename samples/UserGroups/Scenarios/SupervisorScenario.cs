namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Shows supervisor traversal and moves independently of group-based privilege inheritance.</summary>
internal static class SupervisorScenario
{
    private static readonly Guid s_tenantId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000006");
    private static readonly Guid s_groupTreeId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000106");
    private static readonly Guid s_userTreeId = Guid.Parse("2ac9600e-16a4-430e-84d9-000000000206");

    /// <summary>Creates users, selects supervisors without loading an anchor, and moves one reporting line.</summary>
    /// <param name="configuration">The sample-owned database.</param>
    /// <param name="details">Whether output includes persisted hierarchy coordinates.</param>
    /// <param name="cancellationToken">The token used for every database call.</param>
    /// <returns>A task that completes after both hierarchies and group grants have been verified.</returns>
    public static async Task RunAsync(
        SampleDatabaseConfiguration configuration,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(
            "SUPERVISORS: business Position and hidden nested-set coordinates",
            cancellationToken);

        await using var context = UserGroupContexts.Create(configuration);
        var groups = await GroupScenarioData.CreateHierarchyAsync(
            context,
            s_tenantId,
            s_groupTreeId,
            cancellationToken);

        await GroupScenarioData.AssignRoleAsync(
            context,
            s_tenantId,
            groups.ApiTeam.Id,
            "API reader",
            "logs.read",
            cancellationToken);

        await GroupScenarioData.AssignRoleAsync(
            context,
            s_tenantId,
            groups.Finance.Id,
            "Finance reader",
            "invoices.read",
            cancellationToken);

        var chiefExecutive = new User
        {
            Name = "Morgan",
            Position = "Chief executive",
        };

        var engineeringDirector = new User
        {
            Name = "Alex",
            Position = "Director",
        };

        var apiEngineer = new User
        {
            Name = "Taylor",
            Position = "Software engineer",
        };

        var financeDirector = new User
        {
            Name = "Jordan",
            Position = "Director",
        };

        var users = context
            .NestedSet<User>()
            .ForScope(s_tenantId);

        await users.InsertRootAsync(chiefExecutive, s_userTreeId, cancellationToken);
        await users.InsertChildAsync(engineeringDirector, chiefExecutive.Id, cancellationToken);
        await users.InsertChildAsync(apiEngineer, engineeringDirector.Id, cancellationToken);
        await users.InsertChildAsync(financeDirector, chiefExecutive.Id, cancellationToken);

        context.UserGroupMemberships.AddRange(
            new UserGroupMembership
            {
                TenantId = s_tenantId,
                UserId = engineeringDirector.Id,
                UserGroupId = groups.Engineering.Id
            },
            new UserGroupMembership
            {
                TenantId = s_tenantId,
                UserId = apiEngineer.Id,
                UserGroupId = groups.ApiTeam.Id
            },
            new UserGroupMembership
            {
                TenantId = s_tenantId,
                UserId = apiEngineer.Id,
                UserGroupId = groups.Finance.Id
            },
            new UserGroupMembership
            {
                TenantId = s_tenantId,
                UserId = financeDirector.Id,
                UserGroupId = groups.Finance.Id
            });

        await context.SaveChangesAsync(cancellationToken);

        var originalPrivileges = await PrivilegeQueries.EffectiveForUserAsync(
            context,
            s_tenantId,
            apiEngineer.Id,
            cancellationToken);

        SampleConsole.Require(
            originalPrivileges.SequenceEqual(["invoices.read", "logs.read"]),
            "Taylor's grants must combine both assigned groups before the supervisor move.");

        var unassignedPrivileges = await PrivilegeQueries.EffectiveForUserAsync(
            context,
            s_tenantId,
            chiefExecutive.Id,
            cancellationToken);

        SampleConsole.Require(
            unassignedPrivileges.Count == 0,
            "A user without application groups must not inherit grants through the supervisor tree.");

        await SupervisorOutput.TreeAsync(
            context,
            s_tenantId,
            s_userTreeId,
            "Before the reporting-line change:",
            details,
            cancellationToken);

        var originalManagers = await users
            .AncestorsOf(apiEngineer.Id)
            .OrderBy(user => EF.Property<long>(user, UserHierarchyProperties.Left))
            .Select(user => user.Name)
            .ToArrayAsync(cancellationToken);

        SampleConsole.Require(
            originalManagers.SequenceEqual(["Morgan", "Alex"]),
            "The API engineer's original supervisor chain is incorrect.");

        await users.MoveToAsync(apiEngineer.Id, financeDirector.Id, cancellationToken);

        await SupervisorOutput.TreeAsync(
            context,
            s_tenantId,
            s_userTreeId,
            "After Taylor moves below the Finance director:",
            details,
            cancellationToken);

        // WHY: AncestorsOf starts from a key; no tracked User or shadow coordinate must be loaded first.
        var matchingManagers = await users
            .AncestorsOf(apiEngineer.Id)
            .Where(user => user.Position == "Director")
            .Select(user => user.Name)
            .ToArrayAsync(cancellationToken);

        SampleConsole.Require(
            matchingManagers.SequenceEqual(["Jordan"]),
            "Filtering the new supervisor chain by the business Position returned the wrong director.");

        var membershipGroupIds = await context
            .UserGroupMemberships
            .Where(membership => membership.TenantId == s_tenantId && membership.UserId == apiEngineer.Id)
            .OrderBy(membership => membership.UserGroupId)
            .Select(membership => membership.UserGroupId)
            .ToArrayAsync(cancellationToken);

        var privileges = await PrivilegeQueries.EffectiveForUserAsync(
            context,
            s_tenantId,
            apiEngineer.Id,
            cancellationToken);

        SampleConsole.Require(
            membershipGroupIds.SequenceEqual(new[] { groups.ApiTeam.Id, groups.Finance.Id }.OrderBy(id => id))
            && privileges.SequenceEqual(originalPrivileges),
            "Changing a supervisor unexpectedly changed the user's application groups or grants.");

        var report = await users
            .InTree(s_userTreeId)
            .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

        SampleConsole.Require(
            report.IsValid,
            "The supervisor tree is invalid: " + string.Join("; ", report.Issues.Select(issue => issue.Message)));

        await GroupScenarioData.ValidateAsync(context, s_tenantId, s_groupTreeId, cancellationToken);
        await SampleConsole.WriteLineAsync(
            "Filtered supervisor: Jordan (Director); Taylor keeps logs.read and invoices.read from two groups.",
            cancellationToken);
    }
}
