using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Probes ranking-before-validation, visible people, and direct-edge validity rather than ancestor shortcuts.
/// </summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Type-first failure cannot escape upward while eligible-group selection may explicitly skip it (U15-U17).
    /// </summary>
    /// <param name="typeFirst">Whether structural type ranking precedes eligibility validation.</param>
    /// <param name="found">Whether the selected strategy finds a valid supervisor.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TypeSelectionDoesNotFallBackAfterRoutingFailure(
        bool typeFirst,
        bool found
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.MembershipMetadatas.Where(value => value.TenantId == data.TenantId
                && value.UserId == data.Users[3] && value.FieldCode == "Route")
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.Value, "different"), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = typeFirst ? new App.NthTypeGroupThenValidate(1, 1) : new App.NthEligibleGroup([1], 1),
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(
            found ? App.SupervisorResolutionStatus.Found : App.SupervisorResolutionStatus.NotFound,
            origin.Status);
        Assert.Equal(
            found ? new[] { data.Users[5] } : [], origin.Evidence.Select(row => row.CandidateUserId).ToArray());
        Assert.Single(probe.Commands);
    }

    /// <summary>Hidden candidate people are removed before eligible-group ranking (U49,U70).</summary>
    [Fact]
    public async Task HiddenPeopleCannotConsumeTheNearestEligibleGroup()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var hidden = new[] { data.Users[1], data.Users[2] };
        await context.Users.Where(user => user.TenantId == data.TenantId && Enumerable.Contains(hidden, user.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Visible, false), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]);
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(data.Users[3], Assert.Single(Assert.Single(result.Origins).Evidence).CandidateUserId);
        Assert.Single(probe.Commands);
    }

    /// <summary>A hidden intermediate group prevents an apparently complete farther manager path (U48).</summary>
    [Fact]
    public async Task HiddenIntermediateGroupBlocksFartherTypeWinner()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.Groups.Where(group => group.TenantId == data.TenantId && group.Id == data.Groups[3])
            .ExecuteUpdateAsync(setters => setters.SetProperty(group => group.Visible, false), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.NthTypeGroupThenValidate(1, 1) };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorResolutionStatus.NotFound, Assert.Single(result.Origins).Status);
        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Validity includes both endpoints; a closed edge blocks all farther candidates on that origin only (U51-U53).
    /// </summary>
    /// <param name="dayOffset">The evaluated date's offset from the fixture base date.</param>
    /// <param name="found">Whether the date lies inside both inclusive validity bounds.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task DirectEdgeDateBoundsAreInclusive(
        int dayOffset,
        bool found
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var first = new DateOnly(2026, 10, 1);
        await context.GroupRelationPeriods.Where(period => period.TenantId == data.TenantId
                && period.ChildId == data.Groups[5])
            .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.FromDate, first.AddDays(1))
                .SetProperty(period => period.ToDate, first.AddDays(3)), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { OnDay = first.AddDays(dayOffset) };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.HighestBusinessPriority, CancellationToken.None);

        // Assert
        Assert.Equal(found ? App.SupervisorResolutionStatus.Found : App.SupervisorResolutionStatus.NotFound,
            Assert.Single(result.Origins).Status);
        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Role sources qualify exact people/memberships and Either never duplicates the same person (U10-U13).
    /// </summary>
    /// <param name="source">The explicit source of the supervisor role grant.</param>
    [Theory]
    [InlineData("membership")]
    [InlineData("global")]
    [InlineData("either")]
    public async Task ExplicitRoleSourceUsesExactMembership(string source)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(App.SearchStart.IncludeSelf, data.Groups[6]) with
        {
            Roles = source switch
            {
                "membership" => new App.MembershipRoleFilter(data.ManagerRoleId),
                "global" => new App.GlobalUserRole(data.ManagerRoleId),
                _ => new App.EitherRole(data.ManagerRoleId),
            },
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(data.Users[6], Assert.Single(Assert.Single(result.Origins).Evidence).CandidateUserId);
        Assert.Single(probe.Commands);
    }
}
