using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards per-person qualification after selecting one already-qualified group path.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// The selected group cannot admit another person's failed routing/grant or bypass a closed edge.
    /// </summary>
    /// <param name="shape">The complete match or the deliberately invalid person/path evidence.</param>
    [Theory]
    [InlineData("match")]
    [InlineData("routing")]
    [InlineData("grant")]
    [InlineData("edge")]
    public async Task EligibleGroupRehydrationKeepsPersonRulesAndCompletePath(string shape)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var day = new DateOnly(2026, 10, 16);
        var expired = day.AddDays(-1);
        var changed = shape switch
        {
            "match" => 0,
            "routing" => await context.MembershipMetadatas.Where(value => value.TenantId == data.TenantId
                    && value.UserId == data.Users[2] && value.UserGroupId == data.Groups[4]
                    && value.FieldCode == "Route")
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, "another-route"),
                    CancellationToken.None),
            "grant" => await context.MembershipRoles.Where(grant => grant.TenantId == data.TenantId
                    && grant.UserId == data.Users[2] && grant.UserGroupId == data.Groups[4]
                    && grant.RoleId == data.ManagerRoleId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.Visible, false),
                    CancellationToken.None),
            "edge" => await context.GroupRelationPeriods.Where(period => period.TenantId == data.TenantId
                    && period.ParentId == data.Groups[4] && period.ChildId == data.Groups[5])
                .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.ToDate, expired),
                    CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        context.ChangeTracker.Clear();
        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = new App.NthEligibleGroup([3], 1), OnDay = day,
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.All, CancellationToken.None);

        // Assert
        Assert.Equal(shape == "match" ? 0 : 1, changed);
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        var origin = Assert.Single(result.Origins);
        Assert.Equal(shape == "edge" ? App.SupervisorResolutionStatus.NotFound : App.SupervisorResolutionStatus.Found,
            origin.Status);
        var expectedPeople = shape switch
        {
            "match" => new[] { data.Users[1], data.Users[2] },
            "edge" => [],
            _ => new[] { data.Users[1] },
        };

        Assert.Equal(expectedPeople.Order(), origin.Evidence.Select(row => row.CandidateUserId).Order());
        Assert.Null(origin.Continuation);
        Assert.All(origin.Evidence, row =>
        {
            Assert.Equal(data.TenantId, row.TenantId);
            Assert.Equal(data.Users[0], row.SourceUserId);
            Assert.Equal(data.Groups[5], row.StartGroupId);
            Assert.Equal(data.Groups[4], row.FoundGroupId);
            Assert.Equal(data.TreeId, row.StartTreeId);
            Assert.Equal(1, row.Distance);
        });

        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
