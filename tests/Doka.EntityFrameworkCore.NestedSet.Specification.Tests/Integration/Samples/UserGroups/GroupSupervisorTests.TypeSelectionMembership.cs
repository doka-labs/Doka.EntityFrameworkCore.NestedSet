using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class GroupSupervisorTests
{
    /// <summary>Type-first group selection cannot skip a winner whose memberships or local grants all fail.</summary>
    /// <param name="failure">Whether all nearer memberships or role grants are hidden or expired.</param>
    /// <param name="typeFirst">Whether group type is ranked before membership and role eligibility.</param>
    [Theory]
    [InlineData("membership-hidden", true)]
    [InlineData("membership-hidden", false)]
    [InlineData("membership-expired", true)]
    [InlineData("membership-expired", false)]
    [InlineData("grant-hidden", true)]
    [InlineData("grant-hidden", false)]
    [InlineData("grant-expired", true)]
    [InlineData("grant-expired", false)]
    public async Task TypeFirstKeepsRejectedMembershipGroupInsteadOfFallingBack(string failure, bool typeFirst)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var day = new DateOnly(2026, 10, 16);
        var expired = day.AddDays(-1);
        var memberships = context.UserGroupMemberships.Where(row => row.TenantId == data.TenantId
            && row.UserGroupId == data.Groups[2]);

        var grants = context.MembershipRoles.Where(row => row.TenantId == data.TenantId
            && row.UserGroupId == data.Groups[2] && row.RoleId == data.ManagerRoleId);

        var changed = failure switch
        {
            "membership-hidden" => await memberships.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.Visible, false), CancellationToken.None),
            "membership-expired" => await memberships.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ToDate, expired), CancellationToken.None),
            "grant-hidden" => await grants.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.Visible, false), CancellationToken.None),
            "grant-expired" => await grants.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ToDate, expired), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = typeFirst ? new App.NthTypeGroupThenValidate(1, 1) : new App.NthEligibleGroup([1], 1),
            OnDay = day,
        };

        probe.Enabled = true;

        // Act
        // WHY: The nearest Type1 group remains structurally present after all its people lose eligibility.
        // Only the explicitly eligible-first alternative may skip that group and reach Carla farther above.
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(1, changed);
        Assert.True(result.Complete);
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        var origin = Assert.Single(result.Origins);

        Assert.Equal(data.Groups[5], origin.StartGroupId);
        Assert.Equal(typeFirst ? App.SupervisorResolutionStatus.NotFound : App.SupervisorResolutionStatus.Found,
            origin.Status);
        Assert.Equal(typeFirst ? [] : new[] { data.Users[5] },
            origin.Evidence.Select(row => row.CandidateUserId).ToArray());
        Assert.Equal(typeFirst ? null : (int?)data.Groups[0], origin.Continuation?.FoundGroupId);
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
