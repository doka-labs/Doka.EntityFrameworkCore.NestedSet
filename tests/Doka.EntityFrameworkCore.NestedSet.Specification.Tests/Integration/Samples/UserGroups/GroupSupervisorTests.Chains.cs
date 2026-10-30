using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Executes real chain frontiers, routing subjects, and explicit safety bounds.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// The second hop advances above Anna's found group and keeps the configured routing subject (U37-U38,U41,U46,U67).
    /// </summary>
    /// <param name="currentSubject">Whether later hops route from the newly selected supervisor.</param>
    /// <param name="expectedIndex">The fixture user expected at the second hop.</param>
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public async Task OriginalPathAdvancesCursorAndRetainsExplicitRouting(
        bool currentSubject,
        int expectedIndex
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await EnableSnapshotAsync(context);
        var request = ChainRequest(data, currentSubject);
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.FollowOriginalPathAsync(
            context, data.TenantId, request, CancellationToken.None);

        // Assert
        var branch = Assert.Single(result.Branches);
        Assert.True(result.Complete);
        Assert.Equal(App.SupervisorChainStatus.ReachedRequestedHop, branch.Status);
        Assert.Equal(
            new[] { data.Users[1], data.Users[expectedIndex] },
            branch.Hops.Select(hop => hop.Evidence.CandidateUserId));
        Assert.Equal(new[] { data.Groups[5], data.Groups[4] }, branch.Hops.Select(hop => hop.Evidence.StartGroupId));
        Assert.All(branch.Hops, hop => Assert.Equal(data.Groups[5], hop.InitialGroupId));
        Assert.Equal(2, probe.Commands.Count);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// AllMemberships explicitly adds another manager membership path; OriginalPath cannot take it (U39,U43,U59).
    /// </summary>
    /// <param name="expanded">Whether the second hop searches all of the selected supervisor's memberships.</param>
    /// <param name="expectedBranches">The number of separately retained management paths.</param>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task ReExpansionIsExplicitAndPreservesConvergentOrigins(
        bool expanded,
        int expectedBranches
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
        {
            TenantId = data.TenantId,
            UserId = data.Users[1],
            UserGroupId = data.Groups[6],
            LeadershipPriority = 15,
        }, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await EnableSnapshotAsync(context);
        var request = ChainRequest(data, true);
        probe.Enabled = true;

        // Act
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, data.TenantId, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.Equal(expectedBranches, result.Branches.Count);
        Assert.All(result.Branches, branch =>
        {
            Assert.Equal(App.SupervisorChainStatus.ReachedRequestedHop, branch.Status);
            Assert.Equal(data.Users[4], branch.Hops[1].Evidence.CandidateUserId);
            Assert.Equal(data.Groups[5], branch.InitialGroupId);
        });

        Assert.Equal(2, probe.Commands.Count);
        Assert.Equal(expanded ? new[] { data.Groups[4], data.Groups[6] }.Order() : new[] { data.Groups[4] },
            result.Branches.Select(branch => branch.Hops[1].Evidence.StartGroupId).Order());
        Assert.All(result.Branches.SelectMany(branch => branch.Hops), hop =>
        {
            Assert.Equal(data.TreeId, hop.InitialTreeId);
            Assert.Equal(data.TreeId, hop.Evidence.StartTreeId);
        });

        Assert.True(result.Complete);
    }

    /// <summary>Both chain APIs distinguish invisible source users before any successful hop (U65).</summary>
    /// <param name="expanded">Whether each hop re-expands the current user's own memberships.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenInitialSourceCannotProduceAChain(bool expanded)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.Users.Where(user => user.TenantId == data.TenantId && user.Id == data.Users[0])
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Visible, false), CancellationToken.None);
        await EnableSnapshotAsync(context);
        var request = ChainRequest(data, false);
        probe.Enabled = true;

        // Act
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, data.TenantId, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorBatchStatus.SourceNotFound, result.Status);
        Assert.Empty(result.Branches);
        Assert.Single(probe.Commands);
        Assert.True(result.Complete);
    }

    /// <summary>A tiny result budget stops the actual reader before a full candidate list is buffered (U69).</summary>
    [Fact]
    public async Task ReaderBudgetReturnsAnExplicitIncompleteChain()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await EnableSnapshotAsync(context);
        var request = ChainRequest(data, false) with { MaxResultRows = 1 };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.FollowOriginalPathAsync(
            context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.False(result.Complete);
        Assert.Equal(App.SupervisorChainStatus.LimitExceeded, Assert.Single(result.Branches).Status);
        Assert.Single(probe.Commands);
        Assert.InRange(probe.ReaderAdvances, 0, 2);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>An inadequate pre-existing transaction is rejected before the first hop reader (U55).</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason = "SQLite rejects inadequate non-shared-cache transactions before a consumer can receive one; "
            + "the isolation helper is unit-tested separately.")]
    public async Task ReadUncommittedChainTransactionIsRejectedBeforeFirstReader()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var isolation = Engine == "Sqlite" ? IsolationLevel.ReadUncommitted : IsolationLevel.ReadCommitted;
        await using var transaction = await context.Database.BeginTransactionAsync(isolation, CancellationToken.None);
        var request = ChainRequest(data, false);
        probe.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => App.SupervisorQueries.FollowOriginalPathAsync(
            context, data.TenantId, request, CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Empty(probe.Commands);
    }

    /// <summary>Creates the two explicit, finite management rules used by chain oracles.</summary>
    /// <param name="data">The persisted scenario and its explicit first-hop request.</param>
    /// <param name="current">Whether later hops route from the current supervisor.</param>
    private static App.SupervisorChainRequest ChainRequest(
        App.SupervisorScenarioFixture data,
        bool current
    )
    {
        var request = data.Request(group: data.Groups[5]);
        var step = new App.SupervisorStep(request.SearchStart, request.Roles, request.Groups,
            request.Matches, request.OnDay, App.SupervisorSelection.HighestBusinessPriority);

        return new App.SupervisorChainRequest
        {
            SourceUserId = request.SourceUserId,
            StartGroupId = request.StartGroupId,
            Steps = [step, step],
            RoutingOrigin = current ? App.RoutingOrigin.CurrentSubject : App.RoutingOrigin.InitialSubject,
            MaxFrontierStates = 32,
            MaxVisitedStates = 128,
            MaxResultRows = 256,
        };
    }

    /// <summary>Enables the explicit SQL Server snapshot feature only in the test-owned database.</summary>
    /// <param name="context">The context for the test-owned database.</param>
    private Task EnableSnapshotAsync(App.UserGroupContext context)
        => Engine == "SqlServer"
            ? context.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON", CancellationToken.None)
            : Task.CompletedTask;
}
