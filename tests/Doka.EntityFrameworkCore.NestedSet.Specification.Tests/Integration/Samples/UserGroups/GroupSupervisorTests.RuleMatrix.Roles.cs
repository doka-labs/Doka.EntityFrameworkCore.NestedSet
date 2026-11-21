using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Own-team search boundaries stay explicit even when the source also has local and global manager roles
    /// (U08-U09).
    /// </summary>
    /// <param name="self">Whether the search includes the source's own group.</param>
    /// <param name="sourceManager">Whether the source also holds local and global manager roles.</param>
    /// <param name="expectedUser">The fixture user selected under the explicit search boundary.</param>
    [Theory]
    [InlineData(false, false, 5)]
    [InlineData(true, false, 6)]
    [InlineData(false, true, 5)]
    [InlineData(true, true, 6)]
    public async Task ResolveAsync_SourceRole_NeverOverridesConfiguredSearchStart(
        bool self,
        bool sourceManager,
        int expectedUser
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            planProbe: null);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        if (sourceManager)
        {
            await context.MembershipRoles.AddAsync(
                new App.MembershipRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    UserGroupId = data.Groups[6],
                    RoleId = data.ManagerRoleId,
                },
                CancellationToken.None);

            await context.UserRoles.AddAsync(
                new App.UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[0],
                    RoleId = data.ManagerRoleId,
                },
                CancellationToken.None);

            await context.SaveChangesAsync(CancellationToken.None);
        }

        var request = data.Request(
            self
                ? App.SearchStart.IncludeSelf
                : App.SearchStart.ParentsOnly,
            data.Groups[6]);

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(
            context,
            data.TenantId,
            request,
            App.SupervisorSelection.RequireUnique,
            CancellationToken.None);

        // Assert
        var row = Assert.Single(
            Assert.Single(result.Origins)
                .Evidence);

        Assert.Equal(data.Users[expectedUser], row.CandidateUserId);
        Assert.Equal(
            self
                ? 0
                : 2,
            row.Distance);

        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Local grants in another membership, unrelated global roles, and group privilege grants cannot identify a manager
    /// (U10-U13).
    /// </summary>
    /// <param name="shape">The unrelated role evidence introduced as a qualification trap.</param>
    [Theory]
    [InlineData("other-membership")]
    [InlineData("global-without-membership")]
    [InlineData("group-grant")]
    public async Task ResolveAsync_UnrelatedRoleEvidence_CannotQualifyAFoundMembership(
        string shape
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            planProbe: null);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context
            .MembershipRoles
            .Where(role => role.TenantId == data.TenantId && role.UserGroupId == data.Groups[4])
            .ExecuteDeleteAsync(CancellationToken.None);

        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = new App.NthTypeGroupThenValidate(3, 1),
            Matches = [],
        };

        if (shape == "other-membership")
        {
            await context.UserGroupMemberships.AddAsync(
                new App.UserGroupMembership
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    UserGroupId = data.Groups[2],
                    LeadershipPriority = 10,
                },
                CancellationToken.None);

            await context.MembershipRoles.AddAsync(
                new App.MembershipRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    UserGroupId = data.Groups[2],
                    RoleId = data.ManagerRoleId,
                },
                CancellationToken.None);

            await context.UserRoles.AddAsync(
                new App.UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    RoleId = data.ManagerRoleId,
                },
                CancellationToken.None);
        }
        else if (shape == "global-without-membership")
        {
            await context.UserRoles.AddAsync(
                new App.UserRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[5],
                    RoleId = data.ManagerRoleId,
                },
                CancellationToken.None);

            request = request with { Roles = new App.GlobalUserRole(data.ManagerRoleId) };
        }
        else
        {
            await context.GroupRoles.AddAsync(
                new App.UserGroupRole
                {
                    TenantId = data.TenantId,
                    UserGroupId = data.Groups[4],
                    RoleId = data.ManagerRoleId,
                },
                CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(
            context,
            data.TenantId,
            request,
            App.SupervisorSelection.RequireUnique,
            CancellationToken.None);

        // Assert
        Assert.Equal(
            App.SupervisorResolutionStatus.NotFound,
            Assert.Single(result.Origins)
                .Status);

        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Membership, local-grant, and global-grant validity still applies when the structural self path is empty (U52).
    /// </summary>
    /// <param name="assignment">The assignment whose validity expires before the requested date.</param>
    [Theory]
    [InlineData("membership")]
    [InlineData("local")]
    [InlineData("global")]
    public async Task ResolveAsync_SelfPath_DoesNotOverrideAssignmentValidity(
        string assignment
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            planProbe: null);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var expired = new DateOnly(2026, 10, 15);
        if (assignment == "membership")
        {
            await context
                .UserGroupMemberships
                .Where(row => row.TenantId == data.TenantId
                    && row.UserId == data.Users[6]
                    && row.UserGroupId == data.Groups[6])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ToDate, expired), CancellationToken.None);
        }
        else if (assignment == "local")
        {
            await context
                .MembershipRoles
                .Where(row => row.TenantId == data.TenantId
                    && row.UserId == data.Users[6]
                    && row.UserGroupId == data.Groups[6])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ToDate, expired), CancellationToken.None);
        }
        else
        {
            await context
                .UserRoles
                .Where(row => row.TenantId == data.TenantId && row.UserId == data.Users[6])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ToDate, expired), CancellationToken.None);
        }

        var request = data.Request(App.SearchStart.IncludeSelf, data.Groups[6]) with
        {
            Groups = new App.NthTypeGroupThenValidate(4, 1),
            Roles = assignment == "global"
                ? new App.GlobalUserRole(data.ManagerRoleId)
                : new App.MembershipRoleFilter(data.ManagerRoleId),
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(
            context,
            data.TenantId,
            request,
            App.SupervisorSelection.RequireUnique,
            CancellationToken.None);

        // Assert
        Assert.Equal(
            App.SupervisorResolutionStatus.NotFound,
            Assert.Single(result.Origins)
                .Status);

        Assert.Single(probe.Commands);
    }
}
