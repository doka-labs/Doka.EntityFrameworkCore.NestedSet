using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Separates visibility, own memberships, valid catalogs, and missing scalar values across the actual sample APIs.
/// </summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Every API excludes an invisible source and refuses a visible group that is not an own membership (U65).
    /// </summary>
    /// <param name="api">The sample API used to observe the absence outcome.</param>
    /// <param name="foreignStart">Whether the requested group belongs to another user.</param>
    [Theory]
    [InlineData("candidates", false)]
    [InlineData("resolve", false)]
    [InlineData("original", false)]
    [InlineData("expanded", false)]
    [InlineData("candidates", true)]
    [InlineData("resolve", true)]
    [InlineData("original", true)]
    [InlineData("expanded", true)]
    public async Task EveryApiPreservesSourceAndOwnMembershipAbsence(
        string api,
        bool foreignStart
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        if (!foreignStart)
        {
            await context.Users.Where(user => user.TenantId == data.TenantId && user.Id == data.Users[0])
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(user => user.Visible, false), CancellationToken.None);
        }

        await EnableSnapshotAsync(context);
        var start = foreignStart ? data.Groups[0] : data.Groups[5];
        var request = data.Request(group: start);
        var chain = ChainRequest(data, false) with { StartGroupId = start };
        probe.Enabled = true;

        // Act
        var status = api switch
        {
            "candidates" => (await App.SupervisorQueries.Candidates(context, data.TenantId, request)
                .ToArrayAsync(CancellationToken.None)).Length == 0 ? "Empty" : "Rows",
            "resolve" => (await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
                App.SupervisorSelection.RequireUnique, CancellationToken.None)).Status.ToString(),
            "original" => (await App.SupervisorQueries.FollowOriginalPathAsync(context, data.TenantId,
                chain, CancellationToken.None)).Status.ToString(),
            _ => (await App.SupervisorQueries.FollowAllMembershipsAsync(context, data.TenantId,
                chain, CancellationToken.None)).Status.ToString(),
        };

        // Assert
        Assert.Equal(api == "candidates" ? "Empty" : foreignStart ? "NoVisibleMemberships" : "SourceNotFound", status);
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>
    /// An unknown nonempty role/type or unsupported requested scalar kind is an explicit catalog error, not
    /// NoRoleFilter (U26,U28,U68).
    /// </summary>
    /// <param name="invalid">The invalid catalog rule or scalar kind.</param>
    [Theory]
    [InlineData("role")]
    [InlineData("type")]
    [InlineData("kind")]
    public async Task InvalidTenantCatalogRulesCannotSilentlyDisableFilters(string invalid)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]);
        if (invalid == "kind")
        {
            await context.UserMetadataDefinitions
                .Where(definition => definition.TenantId == data.TenantId && definition.FieldCode == "Route")
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(definition => definition.ValueKind, "Int32"),
                    CancellationToken.None);
        }
        else
        {
            request = invalid == "role"
                ? request with { Roles = new App.MembershipRoleFilter(Guid.NewGuid()) }
                : request with { Groups = new App.NthTypeGroupThenValidate(999, 1) };
        }

        probe.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => App.SupervisorQueries.ResolveAsync(context, data.TenantId,
            request, App.SupervisorSelection.RequireUnique, CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>
    /// An explicit role-free type-and-metadata query remains useful after all person grants are removed (U68).
    /// </summary>
    [Fact]
    public async Task ExplicitNoRoleFilterKeepsTypeAndMetadataRules()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.MembershipRoles
            .Where(role => role.TenantId == data.TenantId)
            .ExecuteDeleteAsync(CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with
        {
            Roles = new App.NoRoleFilter(),
            Groups = new App.NthTypeGroupThenValidate(3, 1),
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.HighestBusinessPriority, CancellationToken.None);

        // Assert
        Assert.Equal(data.Users[1], Assert.Single(Assert.Single(result.Origins).Evidence).CandidateUserId);
        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Equal null/empty values, absent values, and a value on the wrong source owner never match (U24-U25).
    /// </summary>
    /// <param name="shape">The missing, empty, or incorrectly owned metadata scenario.</param>
    [Theory]
    [InlineData("both-null")]
    [InlineData("both-empty")]
    [InlineData("both-space")]
    [InlineData("missing")]
    [InlineData("wrong-owner")]
    public async Task MissingValuesAreNotWildcardsAndOwnersAreNotInterchangeable(string shape)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.NthTypeGroupThenValidate(3, 1) };
        if (shape == "missing")
        {
            await context.UserMetadatas.Where(value => value.TenantId == data.TenantId && value.UserId == data.Users[0])
                .ExecuteDeleteAsync(CancellationToken.None);
        }
        else if (shape == "wrong-owner")
        {
            await context.MembershipMetadatas.Where(value => value.TenantId == data.TenantId
                    && value.UserId == data.Users[0] && value.UserGroupId == data.Groups[5])
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.Value, "different"), CancellationToken.None);

            request = request with
            {
                Matches =
                [
                    new(App.MetadataOwner.SourceMembership, "Route", App.MetadataOwner.CandidateMembership, "Route"),
                ],
            };
        }
        else
        {
            var scalar = shape == "both-null" ? null : shape == "both-space" ? " " : string.Empty;
            await context.UserMetadatas.Where(value => value.TenantId == data.TenantId && value.UserId == data.Users[0])
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.Value, scalar), CancellationToken.None);
            await context.MembershipMetadatas
                .Where(value => value.TenantId == data.TenantId && value.UserGroupId == data.Groups[4])
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.Value, scalar), CancellationToken.None);
        }

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(
            shape == "both-space"
                ? App.SupervisorResolutionStatus.Ambiguous
                : App.SupervisorResolutionStatus.NotFound,
            Assert.Single(result.Origins).Status);
        Assert.Single(probe.Commands);
    }
}
