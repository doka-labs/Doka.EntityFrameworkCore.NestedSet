using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks complete application step contracts rather than repeating an identical successful search.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Each hop can independently choose another role and group type, while original provenance stays fixed (U67).
    /// </summary>
    /// <param name="expanded">Whether each hop re-expands the current user's own memberships.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChainStepsApplyDifferentRolesAndGroupTypes(bool expanded)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var departmentRole = Guid.NewGuid();
        var executiveRole = Guid.NewGuid();
        await context.Roles.AddRangeAsync([
            new App.Role
            {
                TenantId = data.TenantId,
                Id = departmentRole,
                Name = "Department lead",
            },
            new App.Role
            {
                TenantId = data.TenantId,
                Id = executiveRole,
                Name = "Executive",
            },
        ], CancellationToken.None);
        await context.MembershipRoles.AddRangeAsync([
            new App.MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[3],
                UserGroupId = data.Groups[2],
                RoleId = departmentRole,
            },
            new App.MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[5],
                UserGroupId = data.Groups[0],
                RoleId = executiveRole,
            },
        ], CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        await EnableSnapshotAsync(context);
        var request = ChainRequest(data, false) with
        {
            Steps =
            [
                new App.SupervisorStep(App.SearchStart.ParentsOnly, new App.MembershipRoleFilter(data.ManagerRoleId),
                    new App.NthTypeGroupThenValidate(3, 1), [], null, App.SupervisorSelection.HighestBusinessPriority),
                new App.SupervisorStep(App.SearchStart.ParentsOnly, new App.MembershipRoleFilter(departmentRole),
                    new App.NthTypeGroupThenValidate(1, 1), [], null, App.SupervisorSelection.RequireUnique),
                new App.SupervisorStep(App.SearchStart.ParentsOnly, new App.MembershipRoleFilter(executiveRole),
                    new App.NthEligibleGroup([1], 1), [], null, App.SupervisorSelection.RequireUnique),
            ],
        };

        probe.Enabled = true;

        // Act
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, data.TenantId, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, data.TenantId, request, CancellationToken.None);

        // Assert
        var branch = Assert.Single(result.Branches);
        Assert.Equal(App.SupervisorChainStatus.ReachedRequestedHop, branch.Status);
        Assert.Equal(new[] { data.Users[1], data.Users[3], data.Users[5] },
            branch.Hops.Select(hop => hop.Evidence.CandidateUserId));
        Assert.Equal(new[] { data.Groups[4], data.Groups[2], data.Groups[0] },
            branch.Hops.Select(hop => hop.Evidence.FoundGroupId));
        Assert.All(branch.Hops, hop => Assert.Equal(data.Groups[5], hop.InitialGroupId));
        Assert.Equal(3, probe.Commands.Count);
        Assert.True(result.Complete);
    }

    /// <summary>
    /// A missing or ambiguous second hop preserves the successful prefix and cannot execute a third reader (U44).
    /// </summary>
    /// <param name="ambiguous">Whether the second hop has tied supervisors instead of no eligible group.</param>
    /// <param name="expanded">Whether each hop re-expands the current user's own memberships.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedLaterHopPreservesPrefixAndStopsItsBranch(
        bool ambiguous,
        bool expanded
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        if (ambiguous)
        {
            await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[2],
                UserGroupId = data.Groups[2],
                LeadershipPriority = 10,
            }, CancellationToken.None);
            await context.MembershipRoles.AddAsync(new App.MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[2],
                UserGroupId = data.Groups[2],
                RoleId = data.ManagerRoleId,
            }, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await EnableSnapshotAsync(context);
        var original = ChainRequest(data, false);
        var second = original.Steps[1] with
        {
            Groups = new App.NthTypeGroupThenValidate(ambiguous ? 1 : 2, 1),
            Selection = App.SupervisorSelection.RequireUnique,
            Matches = [],
        };

        var request = original with { Steps = [original.Steps[0], second, second] };
        probe.Enabled = true;

        // Act
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, data.TenantId, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, data.TenantId, request, CancellationToken.None);

        // Assert
        var branch = Assert.Single(result.Branches);
        Assert.Equal(
            ambiguous ? App.SupervisorChainStatus.Ambiguous : App.SupervisorChainStatus.NoSupervisor,
            branch.Status);
        Assert.Equal(data.Users[1], Assert.Single(branch.Hops).Evidence.CandidateUserId);
        Assert.Equal(2, probe.Commands.Count);
        Assert.True(result.Complete);
    }

    /// <summary>
    /// Both chain APIs forward cancellation before execution and at a later hop's reader boundary (U57).
    /// </summary>
    /// <param name="expanded">Whether each hop re-expands the current user's own memberships.</param>
    /// <param name="laterHop">Whether cancellation begins at the second reader instead of before execution.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChainCancellationIsNeverReportedAsMissingOrComplete(
        bool expanded,
        bool laterHop
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await EnableSnapshotAsync(context);
        using var cancellation = new CancellationTokenSource();
        if (laterHop)
        {
            probe.BeforeReadAsync = async (number, _) =>
            {
                if (number == 2)
                {
                    await cancellation.CancelAsync();
                }
            };
        }
        else
        {
            await cancellation.CancelAsync();
        }

        var request = ChainRequest(data, false);
        probe.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => expanded
            ? App.SupervisorQueries.FollowAllMembershipsAsync(context, data.TenantId, request, cancellation.Token)
            : App.SupervisorQueries.FollowOriginalPathAsync(context, data.TenantId, request, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(laterHop ? 2 : 0, probe.Commands.Count);
        Assert.Null(context.Database.CurrentTransaction);
    }
}
