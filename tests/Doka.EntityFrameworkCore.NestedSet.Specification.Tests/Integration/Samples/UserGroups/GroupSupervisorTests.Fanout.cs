using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Distinguishes shared physical query rows from logical provenance fan-out and retained copies.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Identical expanded searches share one reader but still obey every logical branch budget (U45,U59,U69,U71).
    /// </summary>
    /// <param name="limit">The logical budget dimension constrained by this scenario.</param>
    /// <param name="expectedReaders">The number of physical readers allowed before that limit is reached.</param>
    [Theory]
    [InlineData("frontier", 1)]
    [InlineData("visited", 1)]
    [InlineData("logical", 2)]
    public async Task DeduplicatedPhysicalReadsCannotBypassLogicalBudgets(
        string limit,
        int expectedReaders
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await SeedOriginProfileAsync(context, data, 8, 1);
        await EnableSnapshotAsync(context);
        App.MetadataMatch[] matches =
        [
            new(App.MetadataOwner.SourceUser, "Route", App.MetadataOwner.CandidateMembership, "Route"),
        ];

        var request = new App.SupervisorChainRequest
        {
            SourceUserId = data.Users[0],
            RoutingOrigin = App.RoutingOrigin.InitialSubject,
            MaxFrontierStates = limit == "frontier" ? 4 : 1000,
            MaxVisitedStates = limit == "visited" ? 4 : 1000,
            MaxResultRows = limit == "logical" ? 40 : 1000,
            Steps =
            [
                new(App.SearchStart.IncludeSelf, new App.MembershipRoleFilter(data.ManagerRoleId),
                    new App.NthTypeGroupThenValidate(4, 1), matches, null, App.SupervisorSelection.RequireUnique),
                new(App.SearchStart.ParentsOnly, new App.MembershipRoleFilter(data.ManagerRoleId),
                    new App.NthTypeGroupThenValidate(1, 1), matches, null, App.SupervisorSelection.RequireUnique),
            ],
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.FollowAllMembershipsAsync(
            context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.False(result.Complete);
        Assert.Contains(result.Branches, branch => branch.Status == App.SupervisorChainStatus.LimitExceeded);
        Assert.Equal(expectedReaders, probe.Commands.Count);
        Assert.InRange(probe.ReaderAdvances, 0, limit == "logical" ? 40 : 1000);
        Assert.All(result.Branches.Where(branch => branch.Status == App.SupervisorChainStatus.LimitExceeded),
            branch => Assert.Empty(branch.Hops));
        Assert.Equal(0, probe.Entities);
    }
}
