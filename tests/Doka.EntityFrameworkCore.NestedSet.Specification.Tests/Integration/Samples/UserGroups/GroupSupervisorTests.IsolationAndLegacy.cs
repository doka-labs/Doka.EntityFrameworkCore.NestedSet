using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects tenant/tree routing boundaries and the sample's original independent domain flows.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Matching owner values on a sibling, another tree, or another tenant never repair the selected path (U29).
    /// </summary>
    [Fact]
    public async Task MatchingForeignAndSiblingMetadataCannotSupplyAPathWinner()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var foreign = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var independent = new App.UserGroup
        {
            Name = "Independent matching root",
            TypeId = 3,
        };

        await context.NestedSet<App.UserGroup>().ForScope(data.TenantId)
            .InsertRootAsync(independent, Guid.NewGuid(), CancellationToken.None);
        var sibling = new App.UserGroup
        {
            Name = "Matching sibling",
            TypeId = 3,
        };

        await context.NestedSet<App.UserGroup>().ForScope(data.TenantId)
            .InsertChildAsync(sibling, data.Groups[0], CancellationToken.None);
        await context.Roles.AddAsync(new App.Role
        {
            TenantId = foreign.TenantId,
            Id = data.ManagerRoleId,
            Name = "Same role key in another tenant",
        }, CancellationToken.None);
        await context.MembershipRoles.AddAsync(new App.MembershipRole
        {
            TenantId = foreign.TenantId,
            UserId = foreign.Users[1],
            UserGroupId = foreign.Groups[4],
            RoleId = data.ManagerRoleId,
        }, CancellationToken.None);
        foreach (var trap in new[] { independent, sibling })
        {
            await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = trap.Id,
                LeadershipPriority = 1,
            }, CancellationToken.None);
            await context.MembershipRoles.AddAsync(new App.MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = trap.Id,
                RoleId = data.ManagerRoleId,
            }, CancellationToken.None);
            await context.MembershipMetadatas.AddAsync(new App.MembershipMetadata
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = trap.Id,
                GroupTypeId = 3,
                FieldCode = "Route",
                Value = "lane-blue",
            }, CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        await context.MembershipMetadatas.Where(row => row.TenantId == data.TenantId
                && row.UserGroupId == data.Groups[4] && row.FieldCode == "Route")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "unrelated"), CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.NthEligibleGroup([3], 1) };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorResolutionStatus.NotFound, Assert.Single(result.Origins).Status);
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Original n:m grants and shadow-coordinate user ancestry still work independently of group-supervisor roles
    /// (U64).
    /// </summary>
    [Fact]
    public async Task OriginalPrivilegesAndDirectUserHierarchyRemainIndependent()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.NestedSet<App.User>().ForScope(data.TenantId)
            .MoveToAsync(data.Users[1], data.Users[3], CancellationToken.None);
        await context.NestedSet<App.User>().ForScope(data.TenantId)
            .MoveToAsync(data.Users[0], data.Users[1], CancellationToken.None);
        await App.GroupScenarioData.AssignRoleAsync(context, data.TenantId, data.Groups[0],
            "Inherited reader", "reports.read", CancellationToken.None);
        await App.GroupScenarioData.AssignRoleAsync(context, data.TenantId, data.Groups[6],
            "Team editor", "reports.write", CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Enabled = true;

        // Act
        var grants = await App.PrivilegeQueries.EffectiveForUserAsync(context, data.TenantId,
            data.Users[0], CancellationToken.None);

        var supervisors = await context.NestedSet<App.User>()
            .ForScope(data.TenantId)
            .AncestorsOf(data.Users[0])
            .Select(user => user.Id)
            .ToArrayAsync(CancellationToken.None);

        var groupResult = await App.SupervisorQueries.ResolveAsync(context, data.TenantId,
            data.Request(group: data.Groups[5]),
            App.SupervisorSelection.HighestBusinessPriority, CancellationToken.None);

        // Assert
        Assert.Equal(["reports.read", "reports.write"], grants);
        Assert.Equal([data.Users[3], data.Users[1]], supervisors);
        Assert.Equal(data.Users[1], Assert.Single(Assert.Single(groupResult.Origins).Evidence).CandidateUserId);
        Assert.Equal(3, probe.Commands.Count);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
