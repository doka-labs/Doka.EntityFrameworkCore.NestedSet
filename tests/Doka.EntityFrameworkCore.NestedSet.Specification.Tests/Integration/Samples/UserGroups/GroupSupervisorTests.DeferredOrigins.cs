using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class GroupSupervisorTests
{
    /// <summary>Two deferred candidate queries retain independent origins when their execution order is reversed.</summary>
    [Fact]
    public async Task DeferredCandidatesRetainIndependentRequestOrigins()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var first = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var second = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var firstRequest = first.Request(group: first.Groups[5]);
        var secondRequest = DistinctDeferredRequest(second);
        probe.Enabled = true;
        var firstQuery = App.SupervisorQueries.Candidates(context, first.TenantId, firstRequest);
        var secondQuery = App.SupervisorQueries.Candidates(context, second.TenantId, secondRequest);
        var constructionCommands = probe.Commands.Count;

        // Act
        var secondRows = await secondQuery.ToArrayAsync(CancellationToken.None);
        var secondParameters = probe.ParameterValues.ToArray();
        var firstRows = await firstQuery.ToArrayAsync(CancellationToken.None);
        var firstParameters = probe.ParameterValues.Skip(secondParameters.Length).ToArray();

        // Assert
        Assert.Equal(0, constructionCommands);
        Assert.Equal(2, probe.Commands.Count);
        Assert.Equal(new[] { first.Users[1], first.Users[2] }.Order(),
            firstRows.Select(row => row.CandidateUserId).Order());
        Assert.All(firstRows, row => AssertDeferredCandidate(row, first, firstRequest, first.Groups[4], 1));
        Assert.Equal(second.Users[4], Assert.Single(secondRows).CandidateUserId);
        AssertDeferredCandidate(secondRows[0], second, secondRequest, second.Groups[1], 3);
        AssertDeferredBindings(secondParameters, secondRequest, second.TenantId, probe.Commands[0]);
        AssertDeferredBindings(firstParameters, firstRequest, first.TenantId, probe.Commands[1]);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Cached marker and single-group projections preserve empty and ambiguous source identities.</summary>
    /// <param name="mode">The actual all-evidence or single-group resolution path.</param>
    /// <param name="empty">Whether the first request's date excludes its complete ancestor path.</param>
    [Theory]
    [InlineData("all", true)]
    [InlineData("single", false)]
    [InlineData("single", true)]
    public async Task DeferredResolutionRowsRetainEmptyAndAmbiguousOriginSources(string mode, bool empty)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var first = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var second = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var firstRequest = first.Request(group: first.Groups[5]) with
        {
            OnDay = empty ? new DateOnly(2025, 12, 31) : new DateOnly(2026, 10, 16),
        };

        var secondRequest = DistinctDeferredRequest(second);
        var selection = mode == "all" ? App.SupervisorSelection.All : App.SupervisorSelection.RequireUnique;
        probe.Enabled = true;
        var firstQuery = App.SupervisorQueries.ResolutionRows(context, first.TenantId, firstRequest, selection);
        var secondQuery = App.SupervisorQueries.ResolutionRows(context, second.TenantId, secondRequest, selection);
        var constructionCommands = probe.Commands.Count;

        // Act
        var secondRows = await secondQuery.ToArrayAsync(CancellationToken.None);
        var secondParameters = probe.ParameterValues.ToArray();
        var firstRows = await firstQuery.ToArrayAsync(CancellationToken.None);
        var firstParameters = probe.ParameterValues.Skip(secondParameters.Length).ToArray();
        var secondResult = App.SupervisorQueries.ReadResolution(second.TenantId, secondRows, selection);
        var firstResult = App.SupervisorQueries.ReadResolution(first.TenantId, firstRows, selection);

        // Assert
        Assert.Equal(0, constructionCommands);
        Assert.Equal(2, probe.Commands.Count);
        Assert.Equal(App.SupervisorBatchStatus.Completed, firstResult.Status);
        var firstOrigin = Assert.Single(firstResult.Origins);
        Assert.Equal(first.Groups[5], firstOrigin.StartGroupId);
        Assert.Equal(empty ? App.SupervisorResolutionStatus.NotFound : App.SupervisorResolutionStatus.Ambiguous,
            firstOrigin.Status);
        Assert.Empty(firstOrigin.Evidence);
        Assert.All(firstRows, row => Assert.Equal(firstRequest.SourceUserId, row.SourceUserId));
        var marker = Assert.Single(firstRows.Where(row => row.Kind == 1));
        Assert.Equal(first.Groups[5], marker.StartGroupId);
        Assert.Equal(first.TreeId, marker.StartTreeId);
        Assert.Equal(empty ? 0L : 2L, marker.Count);
        Assert.False(marker.HasEvidence);
        Assert.Equal(App.SupervisorBatchStatus.Completed, secondResult.Status);
        var secondOrigin = Assert.Single(secondResult.Origins);
        Assert.Equal(App.SupervisorResolutionStatus.Found, secondOrigin.Status);
        var evidence = Assert.Single(secondOrigin.Evidence);
        Assert.Equal(second.Users[4], evidence.CandidateUserId);
        AssertDeferredCandidate(evidence, second, secondRequest, second.Groups[1], 3);
        Assert.All(secondRows, row => Assert.Equal(secondRequest.SourceUserId, row.SourceUserId));
        AssertDeferredBindings(secondParameters, secondRequest, second.TenantId, probe.Commands[0]);
        AssertDeferredBindings(firstParameters, firstRequest, first.TenantId, probe.Commands[1]);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Deferred authorized origins remain subject to current source visibility and membership validity.</summary>
    /// <param name="mode">The cached marker or single-group projection to execute.</param>
    /// <param name="rejection">The source or membership condition that must reject the first request.</param>
    [Theory]
    [InlineData("all", "hidden-source")]
    [InlineData("single", "expired-membership")]
    [InlineData("single", "missing-source")]
    public async Task DeferredOriginsDoNotReuseAnotherRequestsAuthorization(string mode, string rejection)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var first = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var second = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var firstRequest = first.Request(group: first.Groups[5]);
        if (rejection == "missing-source")
        {
            firstRequest = firstRequest with { SourceUserId = int.MinValue };
        }

        var secondRequest = DistinctDeferredRequest(second);
        var selection = mode == "all" ? App.SupervisorSelection.All : App.SupervisorSelection.RequireUnique;
        var firstQuery = App.SupervisorQueries.ResolutionRows(context, first.TenantId, firstRequest, selection);
        var secondQuery = App.SupervisorQueries.ResolutionRows(context, second.TenantId, secondRequest, selection);
        // WHY: Build both deferred queries before changing authorization. A cached shape must neither
        // freeze the earlier visible origin nor borrow the other request's valid tenant and source.
        if (rejection == "hidden-source")
        {
            await context.Users.Where(user => user.TenantId == first.TenantId && user.Id == first.Users[0])
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Visible, false), CancellationToken.None);
        }
        else if (rejection == "expired-membership")
        {
            await context.UserGroupMemberships.Where(member => member.TenantId == first.TenantId
                    && member.UserId == first.Users[0] && member.UserGroupId == first.Groups[5])
                .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.ToDate,
                    firstRequest.OnDay!.Value.AddDays(-1)), CancellationToken.None);
        }

        probe.Enabled = true;

        // Act
        var secondRows = await secondQuery.ToArrayAsync(CancellationToken.None);
        var secondParameters = probe.ParameterValues.ToArray();
        var firstRows = await firstQuery.ToArrayAsync(CancellationToken.None);
        var firstParameters = probe.ParameterValues.Skip(secondParameters.Length).ToArray();
        var secondResult = App.SupervisorQueries.ReadResolution(second.TenantId, secondRows, selection);
        var firstResult = App.SupervisorQueries.ReadResolution(first.TenantId, firstRows, selection);

        // Assert
        Assert.Equal(2, probe.Commands.Count);
        Assert.Equal(rejection == "expired-membership"
            ? App.SupervisorBatchStatus.NoVisibleMemberships : App.SupervisorBatchStatus.SourceNotFound,
            firstResult.Status);
        Assert.Empty(firstResult.Origins);
        Assert.DoesNotContain(firstRows, row => row.Kind == 1);
        Assert.Equal(App.SupervisorBatchStatus.Completed, secondResult.Status);
        var origin = Assert.Single(secondResult.Origins);
        Assert.Equal(App.SupervisorResolutionStatus.Found, origin.Status);
        var evidence = Assert.Single(origin.Evidence);
        Assert.Equal(second.Users[4], evidence.CandidateUserId);
        AssertDeferredCandidate(evidence, second, secondRequest, second.Groups[1], 3);
        AssertDeferredBindings(secondParameters, secondRequest, second.TenantId, probe.Commands[0]);
        AssertDeferredBindings(firstParameters, firstRequest, first.TenantId, probe.Commands[1]);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Uses another real source, origin, and validity date with exactly one upstream roleholder.</summary>
    private static App.SupervisorRequest DistinctDeferredRequest(App.SupervisorScenarioFixture data)
        => data.Request(group: data.Groups[4]) with
        {
            SourceUserId = data.Users[1], OnDay = new DateOnly(2026, 10, 17),
        };

    /// <summary>Checks complete occurrence identity without reducing independent requests to person IDs.</summary>
    private static void AssertDeferredCandidate(
        App.SupervisorCandidate row,
        App.SupervisorScenarioFixture data,
        App.SupervisorRequest request,
        int foundGroup,
        int distance
    )
    {
        Assert.Equal(data.TenantId, row.TenantId);
        Assert.Equal(request.SourceUserId, row.SourceUserId);
        Assert.Equal(request.StartGroupId, row.StartGroupId);
        Assert.Equal(data.TreeId, row.StartTreeId);
        Assert.Equal(foundGroup, row.FoundGroupId);
        Assert.Equal(distance, row.Distance);
    }

    /// <summary>Checks each actual command's independent scalar bindings without relying on SQL parameter names.</summary>
    private static void AssertDeferredBindings(
        object?[] values,
        App.SupervisorRequest request,
        Guid tenantId,
        string command
    )
    {
        Assert.Contains(request.SourceUserId, values);
        Assert.Contains(request.StartGroupId!.Value, values);
        var day = request.OnDay!.Value;
        var text = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Contains(values, value => value is DateOnly date && date == day
            || value is DateTime instant && DateOnly.FromDateTime(instant) == day
            || value is string scalar && scalar == text);
        Assert.DoesNotContain(text, command, StringComparison.Ordinal);
        Assert.DoesNotContain(tenantId.ToString("D"), command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tenantId.ToString("N"), command, StringComparison.OrdinalIgnoreCase);
    }
}
