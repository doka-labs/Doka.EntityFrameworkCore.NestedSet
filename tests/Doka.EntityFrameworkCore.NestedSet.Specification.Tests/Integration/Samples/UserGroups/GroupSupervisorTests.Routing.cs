using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies exact metadata owners, same-person AND, and explicit case-sensitive value comparison.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Every explicit owner pair uses its own scalar field and exact membership context (U23-U29).</summary>
    /// <param name="sourceOwner">The exact metadata owner supplying the source scalar.</param>
    /// <param name="candidateOwner">The exact metadata owner supplying the candidate scalar.</param>
    [Theory]
    [InlineData(0, 3)]
    [InlineData(0, 4)]
    [InlineData(0, 5)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(1, 5)]
    [InlineData(2, 3)]
    [InlineData(2, 4)]
    [InlineData(2, 5)]
    public async Task ExactMetadataOwnersSupportDifferentFieldCodes(
        int sourceOwner,
        int candidateOwner
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await AddMetadataAsync(
            context, data, sourceOwner, data.Users[0], data.Groups[5], 4, "ChannelClass", "lane-blue");
        await AddMetadataAsync(
            context, data, candidateOwner, data.Users[1], data.Groups[4], 3, "ManagerRoute", "lane-blue");
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = data.Request(group: data.Groups[5]) with
        {
            Matches =
            [
                new((App.MetadataOwner)sourceOwner, "ChannelClass", (App.MetadataOwner)candidateOwner, "ManagerRoute"),
            ],
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.HighestBusinessPriority, CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(App.SupervisorResolutionStatus.Found, origin.Status);
        Assert.Equal(data.Users[1], Assert.Single(origin.Evidence).CandidateUserId);
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>
    /// Missing, null, empty, differently cased, or unrelated values never become matching wildcards (U25,U28).
    /// </summary>
    /// <param name="value">The candidate value that must not match the source scalar.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("KEY-ABC123")]
    [InlineData("key-other")]
    [InlineData("lane-blue ")]
    public async Task InvalidRoutingValuesCannotMatch(string? value)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await AddMetadataAsync(context, data, 0, data.Users[0], data.Groups[5], 4, "ChannelClass", "lane-blue");
        await AddMetadataAsync(context, data, 4, data.Users[1], data.Groups[4], 3, "ManagerRoute", value);
        await context.SaveChangesAsync(CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = new App.NthTypeGroupThenValidate(3, 1),
            Matches =
            [
                new(App.MetadataOwner.SourceUser, "ChannelClass", App.MetadataOwner.CandidateMembership, "ManagerRoute"),
            ],
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorResolutionStatus.NotFound, Assert.Single(result.Origins).Status);
        Assert.Single(probe.Commands);
    }

    /// <summary>Two routing conditions cannot be satisfied by two different candidate people (U27).</summary>
    /// <param name="matchingPerson">Whether one candidate satisfies both routing conditions.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataAndConditionsCannotMixDifferentPeople(bool matchingPerson)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await AddMetadataAsync(context, data, 0, data.Users[0], data.Groups[5], 4, "ChannelClass", "channel-A");
        await AddMetadataAsync(context, data, 0, data.Users[0], data.Groups[5], 4, "RoutingZone", "zone-A");
        await AddMetadataAsync(context, data, 4, data.Users[1], data.Groups[4], 3, "ChannelClass", "channel-A");
        await AddMetadataAsync(
            context, data, 4, data.Users[1], data.Groups[4], 3, "RoutingZone", matchingPerson ? "zone-A" : "zone-B");
        await AddMetadataAsync(context, data, 4, data.Users[2], data.Groups[4], 3, "ChannelClass", "channel-B");
        await AddMetadataAsync(context, data, 4, data.Users[2], data.Groups[4], 3, "RoutingZone", "zone-A");
        await context.SaveChangesAsync(CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = new App.NthTypeGroupThenValidate(3, 1),
            Matches =
            [
                new(App.MetadataOwner.SourceUser, "ChannelClass", App.MetadataOwner.CandidateMembership, "ChannelClass"),
                new(App.MetadataOwner.SourceUser, "RoutingZone", App.MetadataOwner.CandidateMembership, "RoutingZone"),
            ],
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(
            matchingPerson ? App.SupervisorResolutionStatus.Found : App.SupervisorResolutionStatus.NotFound,
            origin.Status);
        Assert.Equal(
            matchingPerson ? new[] { data.Users[1] } : [],
            origin.Evidence.Select(row => row.CandidateUserId).ToArray());
        Assert.Single(probe.Commands);
    }

    /// <summary>Creates real owner-specific rows using the application's definitions and constraints.</summary>
    /// <param name="context">The context that persists the metadata row.</param>
    /// <param name="data">The tenant-qualified fixture identities.</param>
    /// <param name="owner">The source or candidate metadata owner.</param>
    /// <param name="user">The user owning user or membership metadata.</param>
    /// <param name="group">The group owning group or membership metadata.</param>
    /// <param name="type">The group type qualifying the metadata definition.</param>
    /// <param name="field">The exact metadata field code.</param>
    /// <param name="value">The stored scalar, including an explicit null test value.</param>
    private static async Task AddMetadataAsync(
        App.UserGroupContext context,
        App.SupervisorScenarioFixture data,
        int owner,
        int user,
        int group,
        int type,
        string field,
        string? value
    )
    {
        switch ((App.MetadataOwner)owner)
        {
            case App.MetadataOwner.SourceUser:
            case App.MetadataOwner.CandidateUser:
                await context.UserMetadatas.AddAsync(new App.UserMetadata
                {
                    TenantId = data.TenantId,
                    UserId = user,
                    FieldCode = field,
                    Value = value,
                }, CancellationToken.None);
                break;
            case App.MetadataOwner.SourceMembership:
            case App.MetadataOwner.CandidateMembership:
                await context.MembershipMetadatas.AddAsync(new App.MembershipMetadata
                {
                    TenantId = data.TenantId,
                    UserId = user,
                    UserGroupId = group,
                    GroupTypeId = type,
                    FieldCode = field,
                    Value = value,
                }, CancellationToken.None);
                break;
            case App.MetadataOwner.SourceGroup:
            case App.MetadataOwner.CandidateGroup:
                await context.GroupMetadatas.AddAsync(new App.GroupMetadata
                {
                    TenantId = data.TenantId,
                    UserGroupId = group,
                    GroupTypeId = type,
                    FieldCode = field,
                    Value = value,
                }, CancellationToken.None);
                break;
        }
    }
}
