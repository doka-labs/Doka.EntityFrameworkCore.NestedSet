using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Executes the actual UserGroups sample builders against every qualified relational engine.</summary>
[Collection("Model compatibility")]
public abstract partial class GroupSupervisorTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the existing collection-owned provider database.</summary>
    /// <param name="fixture">The collection-owned database fixture for this provider.</param>
    protected GroupSupervisorTests(IProviderFixture<ModelCompatibilityDatabase> fixture) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// Own memberships remain separate origins with one asynchronous DTO-only reader and no preloads
    /// (U06-U09,U12,U57-U58,U65).
    /// </summary>
    /// <param name="self">Whether each origin includes its own group in the search.</param>
    /// <param name="origins">The expected number of separately evaluated own memberships.</param>
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task OwnOriginsStaySeparateWithoutPreload(
        bool self,
        int origins
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(self ? App.SearchStart.IncludeSelf : App.SearchStart.ParentsOnly);
        probe.Enabled = true;

        // Act
        var batch = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.All, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorBatchStatus.Completed, batch.Status);
        Assert.Equal(origins, batch.Origins.Count);
        Assert.All(batch.Origins.SelectMany(origin => origin.Evidence), row =>
            Assert.NotEqual(data.Users[0], row.CandidateUserId));
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(
            new[] { data.Groups[5], data.Groups[6] }.Order(),
            batch.Origins.Select(row => row.StartGroupId!.Value).Order());
    }

    /// <summary>
    /// SQL distinguishes group, structural, and person ranks without counting roleholders as groups
    /// (U14-U20,U22,U31-U36,U60,U66).
    /// </summary>
    /// <param name="mode">The explicit group or person selection alternative.</param>
    /// <param name="expectedUserIndex">The expected fixture user, or a negative unresolved-status sentinel.</param>
    [Theory]
    [InlineData("group", 3)]
    [InlineData("person", 2)]
    [InlineData("priority", 1)]
    [InlineData("type", 3)]
    [InlineData("unique", -1)]
    [InlineData("missing", -2)]
    public async Task GroupAndPersonSelectionsRemainDistinct(
        string mode,
        int expectedUserIndex
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]);
        var selection = App.SupervisorSelection.RequireUnique;
        request = mode switch
        {
            "group" => request with { Groups = new App.NthEligibleGroup([], 2) },
            "person" => request with { Groups = new App.AllEligibleGroups() },
            "type" => request with { Groups = new App.NthTypeGroupThenValidate(1, 1) },
            "missing" => request with { Groups = new App.NthEligibleGroup([], 99) },
            _ => request,
        };

        selection = mode switch
        {
            "person" => App.SupervisorSelection.NthPerson(2),
            "priority" => App.SupervisorSelection.HighestBusinessPriority,
            _ => selection,
        };

        probe.Enabled = true;

        // Act
        var batch = await App.SupervisorQueries.ResolveAsync(
            context, data.TenantId, request, selection, CancellationToken.None);

        // Assert
        var origin = Assert.Single(batch.Origins);
        Assert.Equal(
            expectedUserIndex == -1
                ? App.SupervisorResolutionStatus.Ambiguous
                : expectedUserIndex == -2
                    ? App.SupervisorResolutionStatus.NotFound
                    : App.SupervisorResolutionStatus.Found,
            origin.Status);
        Assert.Equal(expectedUserIndex < 0 ? [] : new[] { data.Users[expectedUserIndex] },
            origin.Evidence.Select(row => row.CandidateUserId).Distinct().ToArray());
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>
    /// Hidden initial users, missing own membership, and an evaluated zero-hit path are separate statuses (U49,U65).
    /// </summary>
    /// <param name="shape">The source or own-membership absence scenario.</param>
    /// <param name="expected">The expected batch status name.</param>
    [Theory]
    [InlineData("hidden", "SourceNotFound")]
    [InlineData("foreign", "NoVisibleMemberships")]
    [InlineData("empty", "Completed")]
    public async Task SourceAndMembershipStatusesRemainExplicit(
        string shape,
        string expected
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]);
        if (shape == "hidden")
        {
            await context.Users.Where(user => user.TenantId == data.TenantId && user.Id == data.Users[0])
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(user => user.Visible, false), CancellationToken.None);
        }
        else if (shape == "foreign")
        {
            request = request with { StartGroupId = data.Groups[0] };
        }
        else
        {
            request = request with { Groups = new App.NthEligibleGroup([], 99) };
        }

        probe.Enabled = true;

        // Act
        var batch = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(expected, batch.Status.ToString());
        Assert.Single(probe.Commands);
        Assert.All(batch.Origins, origin => Assert.Equal(App.SupervisorResolutionStatus.NotFound, origin.Status));
    }

    /// <summary>Builds the real sample model within the already owned provider database.</summary>
    /// <param name="probe">The command and materialization observer for the consumer operation.</param>
    /// <param name="planProbe">An optional observer for provider query plans.</param>
    private Task<App.UserGroupContext> CreateAsync(
        SupervisorCommandProbe probe,
        EnterpriseProbe? planProbe = null
    )
        => _fixture.CreateContextAsync<App.UserGroupContext>(
            Engine,
            options => new App.UserGroupContext(options),
            planProbe is null ? [probe] : [probe, planProbe]);
}
