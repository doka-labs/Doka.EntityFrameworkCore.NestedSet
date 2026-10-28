using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks server fan-in and bounded frontier SQL with actual sample entities and commands.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Unique selection reads bounded status rows rather than a client candidate list (U34,U72).</summary>
    /// <param name="candidates">The candidate fan-in measured by the shared Arrange/Act/Assert scenario.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public Task UniqueSelectionReducesCandidateFanInOnServer(int candidates) => AssertFanInAsync(candidates);

    /// <summary>
    /// All deliberately reads the requested candidates instead of pretending that broad output is constant memory
    /// (U72).
    /// </summary>
    /// <param name="candidates">The candidate fan-in measured by the shared Arrange/Act/Assert scenario.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public Task AllSelectionReadsTheRequestedCandidateOutput(int candidates) => AssertFanInAsync(candidates, true);

    /// <summary>The deliberately large fan-in case remains an explicit capacity profile (U63,U72).</summary>
    [Fact]
    [Trait("Category", "Capacity")]
    public Task TenThousandCandidatesStillReadOnlyAmbiguousStatus() => AssertFanInAsync(10_000);

    /// <summary>Measures the same actual one-reader unique selection at each candidate cardinality.</summary>
    /// <param name="candidates">The number of qualifying people seeded on the selected origin.</param>
    /// <param name="all">Whether the consumer requests all candidate evidence instead of unique selection.</param>
    private async Task AssertFanInAsync(
        int candidates,
        bool all = false
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var candidateGroup = data.Groups[5];
        await SeedFanInAsync(context, data, candidates);
        var request = data.Request(App.SearchStart.IncludeSelf, candidateGroup) with
        {
            Roles = new App.NoRoleFilter(),
            Groups = new App.NthTypeGroupThenValidate(4, 1),
            Matches = [],
        };

        probe.Enabled = true;

        // Act
        App.SupervisorResolutionBatch result;
        try
        {
            result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
                all ? App.SupervisorSelection.All : App.SupervisorSelection.RequireUnique, CancellationToken.None);
        }
        catch (Exception)
        {
            await QueryPlanTestSupport.WriteEvidenceAsync(
                Engine + "-usergroups-failed-fanin-" + candidates, probe.Commands);

            throw;
        }

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(
            candidates == 0
                ? App.SupervisorResolutionStatus.NotFound
                : candidates == 1 || all
                    ? App.SupervisorResolutionStatus.Found
                    : App.SupervisorResolutionStatus.Ambiguous,
            origin.Status);
        Assert.Single(probe.Commands);
        if (all)
        {
            // WHY: All keeps a separate source and origin marker even when actual evidence is present.
            Assert.InRange(probe.ReaderAdvances, candidates + 2, candidates + 3);
        }
        else
        {
            Assert.InRange(probe.ReaderAdvances, 0, 5);
        }

        Assert.Equal(all ? candidates : candidates == 1 ? 1 : 0, origin.Evidence.Count);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Shares the original native fan-in setup without measuring facade insertion or seeding.</summary>
    /// <param name="context">The actual sample context receiving the independent population.</param>
    /// <param name="data">The existing tenant, source user and source membership.</param>
    /// <param name="candidates">The number of new distinct people linked to the source membership's group.</param>
    /// <returns>The persisted people in independent fixture priority order.</returns>
    private async Task<IReadOnlyList<App.User>> SeedFanInAsync(
        App.UserGroupContext context,
        App.SupervisorScenarioFixture data,
        int candidates
    )
    {
        var candidateGroup = data.Groups[5];
        var sourceId = data.Users[0];
        var rootTree = await context.Users.Where(user => user.TenantId == data.TenantId && user.Id == sourceId)
            .Select(user => EF.Property<Guid>(user, App.UserHierarchyProperties.TreeId))
            .SingleAsync(CancellationToken.None);

        await context.Users.Where(user => user.TenantId == data.TenantId && user.Id == sourceId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => EF.Property<long>(user, App.UserHierarchyProperties.Right), candidates * 2L + 2),
                CancellationToken.None);
        var users = new List<App.User>(candidates);

        for (var index = 0; index < candidates; index++)
        {
            var user = new App.User
            {
                TenantId = data.TenantId,
                Name = $"Fan-in {index}",
                SupervisorId = sourceId,
                Depth = 1,
            };

            await context.Users.AddAsync(user, CancellationToken.None);
            context.Entry(user).Property<Guid>(App.UserHierarchyProperties.TreeId).CurrentValue = rootTree;
            context.Entry(user).Property<long>(App.UserHierarchyProperties.Left).CurrentValue = index * 2L + 2;
            context.Entry(user).Property<long>(App.UserHierarchyProperties.Right).CurrentValue = index * 2L + 3;
            context.Entry(user).Property<long>(App.UserHierarchyProperties.NestedSetPosition).CurrentValue = index;
            users.Add(user);
        }

        // WHY: A precomputed fixture avoids 10,000 individually measured facade inserts; the consumer remains the
        // actual sample query.
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        foreach (var user in users)
        {
            await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = user.Id,
                UserGroupId = candidateGroup,
                LeadershipPriority = 10,
            }, CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await PrepareConsumerStatisticsAsync(context);

        return users;
    }

    /// <summary>
    /// Prepares the actual changed fixture tables outside measurement rather than racing background statistics.
    /// </summary>
    /// <param name="context">The context for the freshly seeded consumer tables.</param>
    private async Task PrepareConsumerStatisticsAsync(App.UserGroupContext context)
    {
        // WHY: Fresh bulk-seeded owner tables need the same explicit statistics preparation as the structural
        // capacity fixtures.
        await QueryPlanTestSupport.RefreshStatisticsAsync<App.UserGroup>(context, Engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<App.User>(context, Engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<App.UserGroupMembership>(context, Engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<App.MembershipRole>(context, Engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<App.MembershipMetadata>(context, Engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<App.UserMetadata>(context, Engine);
        await QueryPlanTestSupport.RefreshStatisticsAsync<App.GroupRelationPeriod>(context, Engine);
    }

    /// <summary>
    /// The deferred four-match build allocates within its explicit consumer budget and never executes I/O (U63).
    /// </summary>
    [Fact]
    public async Task WarmFourMatchQueryBuildHasNoIoOrEntityMaterialization()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var match = new App.MetadataMatch(
            App.MetadataOwner.SourceUser, "Route", App.MetadataOwner.CandidateMembership, "Route");

        var request = data.Request() with { Matches = [match, match, match, match] };
        IQueryable<App.SupervisorCandidate>? query = null;
        Func<object> build = () => query = App.SupervisorQueries.Candidates(context, data.TenantId, request);
        probe.Enabled = true;
        for (var warmup = 0; warmup < 10; warmup++)
        {
            _ = build();
        }

        // Act
        var allocated = QueryConstructionAllocation.Measure(build);

        // Assert
        TestContext.Current.TestOutputHelper?.WriteLine($"Engine={Engine}; four criteria={allocated} B/build.");
        Assert.InRange(allocated, 0, 256 * 1024);
        Assert.NotNull(query?.Expression);
        Assert.Empty(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
