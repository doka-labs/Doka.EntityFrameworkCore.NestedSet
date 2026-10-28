using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Exercises SQL-side ties, type-first ambiguity, and the continuation cursor after person deduplication.
/// </summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Both tied person slots remain ambiguous; a later slot and explicit priority selection retain their own rules
    /// (U32,U35).
    /// </summary>
    /// <param name="mode">The tied person slot or explicit business-priority alternative.</param>
    /// <param name="expected">The expected fixture user, or a negative sentinel for ambiguity.</param>
    [Theory]
    [InlineData("slot1", -1)]
    [InlineData("slot2", -1)]
    [InlineData("slot3", 3)]
    [InlineData("priority", -1)]
    public async Task ServerPersonRanksRetainBusinessTies(
        string mode,
        int expected
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.UserGroupMemberships
            .Where(member => member.TenantId == data.TenantId && member.UserId == data.Users[2])
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(member => member.LeadershipPriority, 10), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.AllEligibleGroups() };
        var selection = mode == "priority"
            ? App.SupervisorSelection.HighestBusinessPriority
            : App.SupervisorSelection.NthPerson(mode[^1] - '0');

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(
            context, data.TenantId, request, selection, CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(
            expected < 0 ? App.SupervisorResolutionStatus.Ambiguous : App.SupervisorResolutionStatus.Found,
            origin.Status);
        Assert.Equal(
            expected < 0 ? [] : new[] { data.Users[expected] },
            origin.Evidence.Select(row => row.CandidateUserId).ToArray());
        Assert.Single(probe.Commands);
    }

    /// <summary>A type-first ambiguous nearer group cannot be replaced by the unique farther group (U17).</summary>
    [Fact]
    public async Task TypeFirstAmbiguityCannotFallBackToAUniqueFartherGroup()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.Groups.Where(group => group.TenantId == data.TenantId && group.Id == data.Groups[2])
            .ExecuteUpdateAsync(setters => setters.SetProperty(group => group.TypeId, 3), CancellationToken.None);
        await context.MembershipMetadatas
            .Where(value => value.TenantId == data.TenantId && value.UserGroupId == data.Groups[2])
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.GroupTypeId, 3), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.NthTypeGroupThenValidate(3, 1) };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorResolutionStatus.Ambiguous, Assert.Single(result.Origins).Status);
        Assert.Empty(result.Origins[0].Evidence);
        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Every person-selection mode uses its documented canonical occurrence for an actual next hop (U66).
    /// </summary>
    /// <param name="mode">The person-selection rule governing the first hop's canonical occurrence.</param>
    /// <param name="expectedPerson">The fixture user expected at the next hop.</param>
    /// <param name="expectedCursor">The first-hop fixture group used as the next search cursor.</param>
    [Theory]
    [InlineData("unique", 3, 4)]
    [InlineData("person", 3, 4)]
    [InlineData("priority", 5, 2)]
    public async Task CanonicalOccurrenceDeterminesTheNextOriginalPathHop(
        string mode,
        int expectedPerson,
        int expectedCursor
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
        {
            TenantId = data.TenantId,
            UserId = data.Users[1],
            UserGroupId = data.Groups[2],
            LeadershipPriority = 1,
        }, CancellationToken.None);
        await context.MembershipRoles.AddAsync(new App.MembershipRole
        {
            TenantId = data.TenantId,
            UserId = data.Users[1],
            UserGroupId = data.Groups[2],
            RoleId = data.ManagerRoleId,
        }, CancellationToken.None);
        await context.MembershipMetadatas.AddAsync(new App.MembershipMetadata
        {
            TenantId = data.TenantId,
            UserId = data.Users[1],
            UserGroupId = data.Groups[2],
            GroupTypeId = 1,
            FieldCode = "Route",
            Value = "lane-blue",
        }, CancellationToken.None);
        await AddMetadataAsync(context, data, 0, data.Users[0], data.Groups[5], 4, "ChannelClass", "only-Anna");
        await AddMetadataAsync(context, data, 4, data.Users[1], data.Groups[4], 3, "ChannelClass", "only-Anna");
        await AddMetadataAsync(context, data, 4, data.Users[1], data.Groups[2], 1, "ChannelClass", "only-Anna");
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await EnableSnapshotAsync(context);
        var standard = data.Request(group: data.Groups[5]);
        var selection = mode switch
        {
            "person" => App.SupervisorSelection.NthPerson(1),
            "priority" => App.SupervisorSelection.HighestBusinessPriority,
            _ => App.SupervisorSelection.RequireUnique,
        };

        var request = ChainRequest(data, false) with
        {
            Steps =
            [
                new(App.SearchStart.ParentsOnly, standard.Roles, new App.AllEligibleGroups(),
                    [new(
                        App.MetadataOwner.SourceUser, "ChannelClass",
                        App.MetadataOwner.CandidateMembership, "ChannelClass")],
                    null, selection),
                new(App.SearchStart.ParentsOnly, standard.Roles, standard.Groups, standard.Matches, null,
                    App.SupervisorSelection.RequireUnique),
            ],
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.FollowOriginalPathAsync(
            context, data.TenantId, request, CancellationToken.None);

        // Assert
        var branch = Assert.Single(result.Branches);
        Assert.Equal(App.SupervisorChainStatus.ReachedRequestedHop, branch.Status);
        Assert.Equal(data.Groups[expectedCursor], branch.Hops[0].Evidence.FoundGroupId);
        Assert.Equal(data.Groups[expectedCursor], branch.Hops[1].Evidence.StartGroupId);
        Assert.Equal(data.Users[expectedPerson], branch.Hops[1].Evidence.CandidateUserId);
        Assert.Equal(2, probe.Commands.Count);
    }
}
