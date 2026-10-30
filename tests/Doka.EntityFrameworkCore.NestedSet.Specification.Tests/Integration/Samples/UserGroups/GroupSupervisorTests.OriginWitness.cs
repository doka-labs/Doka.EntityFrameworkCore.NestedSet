using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class GroupSupervisorTests
{
    /// <summary>Necessary membership keys cannot authorize an invisible or invalid source origin.</summary>
    /// <param name="api">The actual candidate or scalar-resolution API.</param>
    /// <param name="shape">The visible, boundary-valid, or rejected origin condition.</param>
    [Theory]
    [InlineData("candidates", "visible")]
    [InlineData("resolve", "visible")]
    [InlineData("candidates", "boundary")]
    [InlineData("resolve", "boundary")]
    [InlineData("candidates", "missing-source")]
    [InlineData("resolve", "missing-source")]
    [InlineData("candidates", "hidden-membership")]
    [InlineData("resolve", "hidden-membership")]
    [InlineData("candidates", "expired-membership")]
    [InlineData("resolve", "expired-membership")]
    [InlineData("candidates", "future-membership")]
    [InlineData("resolve", "future-membership")]
    [InlineData("candidates", "hidden-group")]
    [InlineData("resolve", "hidden-group")]
    [InlineData("candidates", "missing-membership")]
    [InlineData("resolve", "missing-membership")]
    public async Task NecessaryOriginWitnessRetainsVisibilityAndMembershipValidity(
        string api,
        string shape
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]);
        var membership = context.UserGroupMemberships.Where(row => row.TenantId == data.TenantId
            && row.UserId == data.Users[0] && row.UserGroupId == data.Groups[5]);

        if (shape == "boundary")
        {
            await membership.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.FromDate, request.OnDay)
                .SetProperty(row => row.ToDate, request.OnDay), CancellationToken.None);
        }
        else if (shape == "hidden-membership")
        {
            await membership.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Visible, false),
                CancellationToken.None);
        }
        else if (shape == "expired-membership")
        {
            await membership.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ToDate,
                request.OnDay!.Value.AddDays(-1)), CancellationToken.None);
        }
        else if (shape == "future-membership")
        {
            await membership.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FromDate,
                request.OnDay!.Value.AddDays(1)), CancellationToken.None);
        }
        else if (shape == "hidden-group")
        {
            await context.Groups.Where(group => group.TenantId == data.TenantId && group.Id == data.Groups[5])
                .ExecuteUpdateAsync(setters => setters.SetProperty(group => group.Visible, false), CancellationToken.None);
        }
        else if (shape == "missing-source")
        {
            request = request with { SourceUserId = int.MinValue };
        }
        else if (shape == "missing-membership")
        {
            request = request with { StartGroupId = int.MinValue };
        }

        var accepted = shape is "visible" or "boundary";
        int[] expectedUsers = accepted ? [data.Users[1], data.Users[2]] : [];
        var expectedStatus = accepted
            ? App.SupervisorBatchStatus.Completed
            : shape == "missing-source"
                ? App.SupervisorBatchStatus.SourceNotFound
                : App.SupervisorBatchStatus.NoVisibleMemberships;

        probe.Enabled = true;

        // Act
        var result = await ReadOriginWitnessAsync(context, data.TenantId, request, api);

        // Assert
        Assert.Equal(expectedUsers.Order(), result.Evidence.Select(row => row.CandidateUserId).Order());
        Assert.All(result.Evidence, row =>
        {
            Assert.Equal(data.TenantId, row.TenantId);
            Assert.Equal(data.Users[0], row.SourceUserId);
            Assert.Equal(data.Groups[5], row.StartGroupId);
            Assert.Equal(data.TreeId, row.StartTreeId);
            Assert.Equal(data.Groups[4], row.FoundGroupId);
            Assert.Equal(1, row.Distance);
        });

        if (api == "resolve")
        {
            Assert.Equal(expectedStatus, result.Status);
            Assert.Equal(accepted ? 1 : 0, result.Origins.Length);
        }

        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Selective origin keys preserve repeated person evidence and an empty independent-tree origin.</summary>
    /// <param name="api">The actual candidate or scalar-resolution API.</param>
    [Theory]
    [InlineData("candidates")]
    [InlineData("resolve")]
    public async Task NecessaryOriginWitnessPreservesIndependentOriginsAndEvidenceBag(string api)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var independentTree = Guid.NewGuid();
        var emptyOrigin = new App.UserGroup { Name = "Independent empty origin", TypeId = 4 };
        await context.NestedSet<App.UserGroup>().ForScope(data.TenantId)
            .InsertRootAsync(emptyOrigin, independentTree, CancellationToken.None);
        await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
        {
            TenantId = data.TenantId,
            UserId = data.Users[0],
            UserGroupId = emptyOrigin.Id,
        }, CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = data.Request() with { Groups = new App.AllEligibleGroups() };
        probe.Enabled = true;

        // Act
        var result = await ReadOriginWitnessAsync(context, data.TenantId, request, api);

        // Assert
        Assert.Equal(5, result.Evidence.Length);
        Assert.Equal(new[] { data.Groups[5], data.Groups[6] }.Order(),
            result.Evidence.Where(row => row.CandidateUserId == data.Users[5])
                .Select(row => row.StartGroupId).Order());
        Assert.Equal(4, result.Evidence.Count(row => row.StartGroupId == data.Groups[5]));
        Assert.Equal(data.Users[5], Assert.Single(result.Evidence.Where(row => row.StartGroupId == data.Groups[6]))
            .CandidateUserId);
        Assert.All(result.Evidence, row =>
        {
            Assert.Equal(data.TenantId, row.TenantId);
            Assert.Equal(data.TreeId, row.StartTreeId);
        });
        Assert.DoesNotContain(result.Evidence, row => row.StartGroupId == emptyOrigin.Id
            || row.FoundGroupId == emptyOrigin.Id);

        if (api == "resolve")
        {
            Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
            Assert.Equal(3, result.Origins.Length);
            Assert.Equal(App.SupervisorResolutionStatus.NotFound,
                Assert.Single(result.Origins.Where(origin => origin.StartGroupId == emptyOrigin.Id)).Status);
        }

        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Executes one actual public sample path without mixing candidate and resolution actions.</summary>
    private static async Task<(App.SupervisorBatchStatus? Status, App.SupervisorResolution[] Origins,
        App.SupervisorCandidate[] Evidence)> ReadOriginWitnessAsync(
        App.UserGroupContext context,
        Guid tenantId,
        App.SupervisorRequest request,
        string api
    )
    {
        if (api == "candidates")
        {
            var candidates = await App.SupervisorQueries.Candidates(context, tenantId, request)
                .ToArrayAsync(CancellationToken.None);

            return (null, [], candidates);
        }

        var result = await App.SupervisorQueries.ResolveAsync(context, tenantId, request,
            App.SupervisorSelection.All, CancellationToken.None);

        return (result.Status, result.Origins.ToArray(), result.Origins.SelectMany(origin => origin.Evidence).ToArray());
    }
}
