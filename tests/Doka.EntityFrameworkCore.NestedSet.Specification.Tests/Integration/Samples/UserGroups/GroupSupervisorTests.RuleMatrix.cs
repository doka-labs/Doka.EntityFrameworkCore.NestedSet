using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Supplies independent adversarial cases for origin, role, rank, visibility, and canonical-evidence boundaries.
/// </summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Overlapping memberships on the same ancestry line remain two independent origins (U07,U19).</summary>
    [Fact]
    public async Task ResolveAsync_OverlappingAncestorOrigins_KeepTheirOwnGroupRanks()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            planProbe: null);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.UserGroupMemberships.AddAsync(
            new App.UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[0],
                UserGroupId = data.Groups[4],
                LeadershipPriority = 50,
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        var request = data.Request(App.SearchStart.IncludeSelf) with { Groups = new App.NthEligibleGroup([], 2) };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(
            context,
            data.TenantId,
            request,
            App.SupervisorSelection.RequireUnique,
            CancellationToken.None);

        // Assert
        Assert.Equal(3, result.Origins.Count);
        Assert.Equal(
            data.Users[3],
            Assert.Single(
                    result.Origins.Single(origin => origin.StartGroupId == data.Groups[4])
                        .Evidence)
                .CandidateUserId);

        Assert.Equal(
            data.Users[3],
            Assert.Single(
                    result.Origins.Single(origin => origin.StartGroupId == data.Groups[5])
                        .Evidence)
                .CandidateUserId);

        Assert.Equal(
            data.Users[5],
            Assert.Single(
                    result.Origins.Single(origin => origin.StartGroupId == data.Groups[6])
                        .Evidence)
                .CandidateUserId);

        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Repeated type identities rank by stored path distance and do not turn a failed middle type into a farther winner
    /// (U14,U20).
    /// </summary>
    /// <param name="number">The one-based structural rank among matching group types.</param>
    /// <param name="expected">The expected fixture user, or a negative sentinel for no supervisor.</param>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, -1)]
    [InlineData(3, 5)]
    public async Task ResolveAsync_RepeatedTypes_UseTheirOwnStructuralRank(
        int number,
        int expected
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            planProbe: null);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.NthTypeGroupThenValidate(1, number) };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(
            context,
            data.TenantId,
            request,
            App.SupervisorSelection.RequireUnique,
            CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(
            expected < 0
                ? App.SupervisorResolutionStatus.NotFound
                : App.SupervisorResolutionStatus.Found,
            origin.Status);

        Assert.Equal(
            expected < 0
                ? []
                : new[] { data.Users[expected] },
            origin
                .Evidence
                .Select(row => row.CandidateUserId)
                .ToArray());

        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// One person in multiple ancestor groups has retained evidence and a rule-specific continuation cursor (U33,U66).
    /// </summary>
    /// <param name="bestPriority">Whether continuation follows business priority instead of nearest evidence.</param>
    /// <param name="expectedGroup">The fixture group supplying the canonical continuation.</param>
    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 2)]
    public async Task ResolveAsync_RepeatedPerson_KeepsEvidenceAndCanonicalContinuation(
        bool bestPriority,
        int expectedGroup
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken,
            planProbe: null);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var hidden = new[] { data.Users[2], data.Users[3], data.Users[5] };
        await context
            .Users
            .Where(user => user.TenantId == data.TenantId && Enumerable.Contains(hidden, user.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Visible, false), CancellationToken.None);

        await context.UserGroupMemberships.AddAsync(
            new App.UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = data.Groups[2],
                LeadershipPriority = 1,
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

        await context.MembershipMetadatas.AddAsync(
            new App.MembershipMetadata
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = data.Groups[2],
                GroupTypeId = 1,
                FieldCode = "Route",
                Value = "lane-blue",
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.AllEligibleGroups() };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(
            context,
            data.TenantId,
            request,
            bestPriority
                ? App.SupervisorSelection.HighestBusinessPriority
                : App.SupervisorSelection.RequireUnique,
            CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(App.SupervisorResolutionStatus.Found, origin.Status);
        Assert.Equal(2, origin.Evidence.Count);
        Assert.All(origin.Evidence, row => Assert.Equal(data.Users[1], row.CandidateUserId));
        Assert.Equal(data.Groups[expectedGroup], origin.Continuation!.FoundGroupId);
        Assert.Single(probe.Commands);
    }
}
