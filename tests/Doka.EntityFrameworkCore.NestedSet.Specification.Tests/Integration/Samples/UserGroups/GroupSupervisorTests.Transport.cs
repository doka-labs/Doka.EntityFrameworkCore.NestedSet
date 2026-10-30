using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects the SQL overflow probe independently of logical path-growth budgets.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Both entry points reject an unrepresentable overflow probe before opening a reader.</summary>
    /// <param name="expanded">Whether the chain re-expands the selected person's own memberships.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrepresentableReaderBudgetIsRejectedBeforeIo(bool expanded)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = ChainRequest(data, false) with { MaxResultRows = int.MaxValue };
        probe.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => expanded
            ? App.SupervisorQueries.FollowAllMembershipsAsync(context, data.TenantId, request, CancellationToken.None)
            : App.SupervisorQueries.FollowOriginalPathAsync(context, data.TenantId, request, CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Empty(probe.Commands);
        Assert.Equal(0, probe.ReaderAdvances);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// A zero-hit origin transports exactly a source marker and its explicit outcome; one extra row detects overflow.
    /// </summary>
    /// <param name="expanded">Whether the chain re-expands the selected person's own memberships.</param>
    /// <param name="maximumRows">The exact, insufficient, or largest representable physical reader budget.</param>
    /// <param name="complete">Whether both physical status rows fit the requested budget.</param>
    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    [InlineData(false, int.MaxValue - 1, true)]
    [InlineData(true, int.MaxValue - 1, true)]
    public async Task ReaderBudgetAcceptsExactRowsAndDetectsOneOverflow(
        bool expanded,
        int maximumRows,
        bool complete
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await EnableSnapshotAsync(context);
        var initial = ChainRequest(data, false);
        var request = initial with
        {
            Steps = [initial.Steps[0] with { Groups = new App.NthEligibleGroup([], 99) }],
            MaxResultRows = maximumRows,
        };

        probe.Enabled = true;

        // Act
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, data.TenantId, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.Equal(complete, result.Complete);
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        var branch = Assert.Single(result.Branches);
        Assert.Equal(complete ? App.SupervisorChainStatus.NoSupervisor : App.SupervisorChainStatus.LimitExceeded,
            branch.Status);
        Assert.Empty(branch.Hops);
        Assert.Single(probe.Commands);
        Assert.InRange(probe.ReaderAdvances, 2, 3);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>All retains an independent origin marker and every genuine evidence occurrence in one reader.</summary>
    /// <param name="found">Whether the requested group ordinal admits the two matching people.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllTransportsOriginMarkersSeparatelyFromEvidence(bool found)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with
        {
            Groups = new App.NthEligibleGroup([3], found ? 1 : 99),
        };

        probe.Enabled = true;

        // Act
        var rows = await App.SupervisorQueries.ResolutionRows(
            context, data.TenantId, request, App.SupervisorSelection.All).ToArrayAsync(CancellationToken.None);

        var result = App.SupervisorQueries.ReadResolution(data.TenantId, rows, App.SupervisorSelection.All);

        // Assert
        Assert.Equal(found ? 4 : 2, rows.Length);
        Assert.True(Assert.Single(rows.Where(row => row.Kind == 0)).CatalogValid);
        var marker = Assert.Single(rows.Where(row => row.Kind == 1 && !row.HasEvidence));
        Assert.Equal(0L, marker.Count);
        Assert.Equal(data.Users[0], marker.SourceUserId);
        Assert.Equal(data.Groups[5], marker.StartGroupId);
        Assert.Equal(data.TreeId, marker.StartTreeId);
        var evidence = rows.Where(row => row.HasEvidence).ToArray();
        Assert.Equal(found ? 2 : 0, evidence.Length);
        Assert.All(evidence, row =>
        {
            Assert.Equal(1L, row.Count);
            Assert.Equal(data.Users[0], row.SourceUserId);
            Assert.Equal(data.Groups[5], row.StartGroupId);
            Assert.Equal(data.TreeId, row.StartTreeId);
            Assert.Equal(data.Groups[4], row.FoundGroupId);
            Assert.Equal(1, row.Distance);
        });

        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        var origin = Assert.Single(result.Origins);
        Assert.Equal(found ? App.SupervisorResolutionStatus.Found : App.SupervisorResolutionStatus.NotFound,
            origin.Status);
        Assert.Null(origin.Continuation);
        Assert.Equal(data.Users.Skip(1).Take(found ? 2 : 0).Order(),
            origin.Evidence.Select(row => row.CandidateUserId).Order());
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// All's real source, origin, and evidence rows each consume the unchanged physical reader budget.
    /// </summary>
    /// <param name="expanded">Whether the chain re-expands the selected person's own memberships.</param>
    /// <param name="maximumRows">The insufficient, exact, or largest representable physical row budget.</param>
    /// <param name="complete">Whether all three physical rows fit the budget.</param>
    [Theory]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 3, true)]
    [InlineData(true, 3, true)]
    [InlineData(false, int.MaxValue - 1, true)]
    [InlineData(true, int.MaxValue - 1, true)]
    public async Task AllReaderBudgetCountsMarkersAndOneOverflow(
        bool expanded,
        int maximumRows,
        bool complete
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.MembershipRoles.Where(role => role.TenantId == data.TenantId
                && role.UserId == data.Users[2] && role.UserGroupId == data.Groups[4]
                && role.RoleId == data.ManagerRoleId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.Visible, false), CancellationToken.None);
        await EnableSnapshotAsync(context);
        var initial = ChainRequest(data, false);
        var request = initial with
        {
            Steps = [initial.Steps[0] with
            {
                Groups = new App.NthEligibleGroup([3], 1),
                Selection = App.SupervisorSelection.All,
            }],
            MaxResultRows = maximumRows,
        };

        probe.Enabled = true;

        // Act
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, data.TenantId, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.Equal(complete, result.Complete);
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        var branch = Assert.Single(result.Branches);
        Assert.Equal(complete
            ? App.SupervisorChainStatus.ReachedRequestedHop : App.SupervisorChainStatus.LimitExceeded, branch.Status);
        if (complete)
        {
            var hop = Assert.Single(branch.Hops);
            Assert.Equal(data.Users[1], hop.Evidence.CandidateUserId);
            Assert.Equal(data.Groups[5], hop.InitialGroupId);
            Assert.Equal(data.TreeId, hop.InitialTreeId);
        }
        else
        {
            Assert.Empty(branch.Hops);
        }

        Assert.Single(probe.Commands);
        Assert.InRange(probe.ReaderAdvances, 3, 4);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// Exhausting All's marker budget prevents a second-hop reader from opening in either chain mode.
    /// </summary>
    /// <param name="expanded">Whether the next hop would re-expand the selected person's own memberships.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllMarkerExhaustionStopsBeforeTheNextReader(bool expanded)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.MembershipRoles.Where(role => role.TenantId == data.TenantId
                && role.UserId == data.Users[2] && role.UserGroupId == data.Groups[4]
                && role.RoleId == data.ManagerRoleId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.Visible, false), CancellationToken.None);
        await EnableSnapshotAsync(context);
        var initial = ChainRequest(data, false);
        var request = initial with
        {
            Steps = [initial.Steps[0] with
            {
                Groups = new App.NthEligibleGroup([3], 1),
                Selection = App.SupervisorSelection.All,
            }, initial.Steps[1]],
            MaxResultRows = 3,
        };

        probe.Enabled = true;

        // Act
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, data.TenantId, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.False(result.Complete);
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        var branch = Assert.Single(result.Branches);
        Assert.Equal(App.SupervisorChainStatus.LimitExceeded, branch.Status);
        Assert.Empty(branch.Hops);
        Assert.Single(probe.Commands);
        Assert.InRange(probe.ReaderAdvances, 3, 4);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
