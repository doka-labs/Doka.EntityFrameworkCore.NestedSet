using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects same-candidate AND within and across grouped metadata-owner rowsets.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Owner-pair grouping preserves every condition on one person even when different people satisfy separate pairs.
    /// </summary>
    /// <param name="shape">The complete match or the deliberately split qualification trap.</param>
    /// <param name="reverse">Whether the caller reverses the order of its owner pairs and field criteria.</param>
    [Theory]
    [InlineData("match", false)]
    [InlineData("match", true)]
    [InlineData("within-pair", false)]
    [InlineData("within-pair", true)]
    [InlineData("between-pairs", false)]
    [InlineData("between-pairs", true)]
    public async Task MixedOwnerRowsetsKeepEveryConditionOnTheSameCandidate(string shape, bool reverse)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await AddMetadataAsync(context, data, 0, data.Users[0], data.Groups[5], 4, "ChannelClass", "channel-A");
        await AddMetadataAsync(context, data, 0, data.Users[0], data.Groups[5], 4, "RoutingZone", "zone-A");
        await AddMetadataAsync(context, data, 1, data.Users[0], data.Groups[5], 4, "ChannelClass", "member-A");
        await AddMetadataAsync(context, data, 2, data.Users[0], data.Groups[5], 4, "RoutingZone", "group-A");
        await AddMetadataAsync(context, data, 4, data.Users[1], data.Groups[4], 3, "ChannelClass", "channel-A");
        await AddMetadataAsync(context, data, 4, data.Users[1], data.Groups[4], 3, "RoutingZone",
            shape == "within-pair" ? "zone-B" : "zone-A");
        await AddMetadataAsync(context, data, 3, data.Users[1], data.Groups[4], 3, "ManagerRoute",
            shape == "between-pairs" ? "member-B" : "member-A");
        await AddMetadataAsync(context, data, 4, data.Users[2], data.Groups[4], 3, "ChannelClass", "channel-B");
        await AddMetadataAsync(context, data, 4, data.Users[2], data.Groups[4], 3, "RoutingZone", "zone-A");
        await AddMetadataAsync(context, data, 3, data.Users[2], data.Groups[4], 3, "ManagerRoute", "member-A");
        await AddMetadataAsync(context, data, 5, data.Users[1], data.Groups[4], 3, "ManagerRoute", "group-A");
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        App.MetadataMatch[] matches =
        [
            new(App.MetadataOwner.SourceUser, "ChannelClass", App.MetadataOwner.CandidateMembership, "ChannelClass"),
            new(App.MetadataOwner.SourceUser, "RoutingZone", App.MetadataOwner.CandidateMembership, "RoutingZone"),
            new(App.MetadataOwner.SourceMembership, "ChannelClass", App.MetadataOwner.CandidateUser, "ManagerRoute"),
            new(App.MetadataOwner.SourceGroup, "RoutingZone", App.MetadataOwner.CandidateGroup, "ManagerRoute"),
        ];

        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = new App.NthTypeGroupThenValidate(3, 1),
            Matches = reverse ? matches.Reverse().ToArray() : matches,
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(shape == "match" ? App.SupervisorResolutionStatus.Found : App.SupervisorResolutionStatus.NotFound,
            origin.Status);
        Assert.Equal(shape == "match" ? new[] { data.Users[1] } : [],
            origin.Evidence.Select(evidence => evidence.CandidateUserId).ToArray());
        Assert.Single(probe.Commands);
        Assert.True(probe.HasStringParameterElement("ChannelClass"));
        Assert.True(probe.HasStringParameterElement("ManagerRoute"));
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// The last of sixteen distinct field pairs is mandatory; missing values never turn All into a wildcard.
    /// </summary>
    /// <param name="shape">The final pair's exact, missing, invalid, or single-space comparison.</param>
    [Theory]
    [InlineData("match")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("case")]
    [InlineData("padding")]
    [InlineData("space")]
    public async Task FinalRoutingPairRemainsRequiredInsideTheOwnerRowset(string shape)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await SeedOriginProfileAsync(context, data, 1, s_profileFields.Length);
        var finalField = s_profileFields[^1];
        var target = context.MembershipMetadatas.Where(value => value.TenantId == data.TenantId
            && value.UserId == data.Users[1] && value.FieldCode == finalField);

        if (shape == "missing")
        {
            await target.ExecuteDeleteAsync(CancellationToken.None);
        }
        else
        {
            var value = shape switch
            {
                "null" => null,
                "empty" => string.Empty,
                "case" => "KEY-ABC123",
                "padding" => "lane-blue ",
                "space" => " ",
                _ => "lane-blue",
            };

            await target.ExecuteUpdateAsync(setters => setters.SetProperty(metadata => metadata.Value, value),
                CancellationToken.None);
        }

        if (shape == "space")
        {
            await context.UserMetadatas.Where(value => value.TenantId == data.TenantId
                    && value.UserId == data.Users[0] && value.FieldCode == finalField)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, " "), CancellationToken.None);
        }

        var request = data.Request(App.SearchStart.IncludeSelf) with
        {
            Groups = new App.NthTypeGroupThenValidate(4, 1),
            Matches = s_profileFields.Select(field => new App.MetadataMatch(
                App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        var matches = shape is "match" or "space";
        Assert.Equal(matches ? App.SupervisorResolutionStatus.Found : App.SupervisorResolutionStatus.NotFound,
            origin.Status);
        Assert.Equal(matches ? new[] { data.Users[1] } : [],
            origin.Evidence.Select(evidence => evidence.CandidateUserId).ToArray());
        Assert.Single(probe.Commands);
        Assert.True(probe.HasStringParameterElement(finalField));
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
