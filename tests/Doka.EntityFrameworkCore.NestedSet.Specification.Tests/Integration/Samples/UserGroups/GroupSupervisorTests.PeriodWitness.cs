using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Protects overlapping period witnesses and independent expanded paths after populated-table history.
/// </summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Overlapping periods admit each path once, while an expired incoming edge blocks only that branch.
    /// </summary>
    /// <param name="blocked">Whether every incoming G3-to-G4 period expires before the requested day.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingPeriodWitnessesPreserveIndependentExpandedOrigins(bool blocked)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        for (var index = 0; index < 25; index++)
        {
            await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        }

        // WHY: A previous expanded-chain rejection appeared only after shared tables contained earlier tenants.
        // Populate that history before the authorized fixture so fresh low identities cannot hide the seam.
        var foreign = context.NestedSet<App.UserGroup>().ForScope(Guid.NewGuid());
        for (var index = 0; index < 3; index++)
        {
            await foreign.InsertRootAsync(new App.UserGroup { Name = "Foreign history root " + index },
                Guid.NewGuid(), CancellationToken.None);
        }

        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
        {
            TenantId = data.TenantId, UserId = data.Users[1], UserGroupId = data.Groups[6], LeadershipPriority = 15,
        }, CancellationToken.None);
        (int Parent, int Child)[] overlappingEdges =
        [
            (data.Groups[0], data.Groups[1]),
            (data.Groups[1], data.Groups[2]),
            (data.Groups[2], data.Groups[3]),
            (data.Groups[3], data.Groups[4]),
            (data.Groups[4], data.Groups[5]),
            (data.Groups[1], data.Groups[6]),
        ];

        foreach (var edge in overlappingEdges)
        {
            await context.GroupRelationPeriods.AddAsync(new App.GroupRelationPeriod
            {
                TenantId = data.TenantId, ParentId = edge.Parent, ChildId = edge.Child,
                FromDate = new DateOnly(2026, 2, 1),
            }, CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        if (blocked)
        {
            // WHY: Expire both overlapping windows. G4-to-G5 stays valid for the first hop, and the
            // independent G1-to-G6 path must still admit Dana when the later G4-origin path is blocked.
            await context.GroupRelationPeriods.Where(period => period.TenantId == data.TenantId
                    && period.ParentId == data.Groups[3] && period.ChildId == data.Groups[4])
                .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.ToDate,
                    new DateOnly(2026, 10, 15)), CancellationToken.None);
        }

        await EnableSnapshotAsync(context);
        var request = ChainRequest(data, true);
        int[] expectedOrigins = blocked ? [data.Groups[6]] : [data.Groups[4], data.Groups[6]];
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.FollowAllMembershipsAsync(
            context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.True(result.Complete);
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        Assert.Equal(2, result.Branches.Count);
        Assert.All(result.Branches, branch =>
        {
            Assert.Equal(data.Groups[5], branch.InitialGroupId);
            Assert.NotEmpty(branch.Hops);
            var first = branch.Hops[0];
            Assert.Equal(1, first.Number);
            Assert.Equal(data.Users[0], first.InitialUserId);
            Assert.Equal(data.Groups[5], first.InitialGroupId);
            Assert.Equal(data.TreeId, first.InitialTreeId);
            Assert.Equal(data.TenantId, first.Evidence.TenantId);
            Assert.Equal(data.Users[0], first.Evidence.SourceUserId);
            Assert.Equal(data.Groups[5], first.Evidence.StartGroupId);
            Assert.Equal(data.TreeId, first.Evidence.StartTreeId);
            Assert.Equal(data.Groups[4], first.Evidence.FoundGroupId);
            Assert.Equal(data.Users[1], first.Evidence.CandidateUserId);
            Assert.Equal(1, first.Evidence.Distance);
        });

        var continued = result.Branches
            .Where(branch => branch.Status == App.SupervisorChainStatus.ReachedRequestedHop).ToArray();

        Assert.Equal(expectedOrigins.Order(), continued.Select(branch => branch.Hops[1].Evidence.StartGroupId).Order());
        Assert.All(continued, branch =>
        {
            Assert.Equal(2, branch.Hops.Count);
            var second = branch.Hops[1];
            Assert.Equal(2, second.Number);
            Assert.Equal(data.Users[0], second.InitialUserId);
            Assert.Equal(data.Groups[5], second.InitialGroupId);
            Assert.Equal(data.TreeId, second.InitialTreeId);
            Assert.Equal(data.TenantId, second.Evidence.TenantId);
            Assert.Equal(data.Users[1], second.Evidence.SourceUserId);
            Assert.Equal(data.TreeId, second.Evidence.StartTreeId);
            Assert.Equal(data.Groups[1], second.Evidence.FoundGroupId);
            Assert.Equal(data.Users[4], second.Evidence.CandidateUserId);
            Assert.Equal(second.Evidence.StartGroupId == data.Groups[4] ? 3 : 1, second.Evidence.Distance);
        });

        var rejected = result.Branches
            .Where(branch => branch.Status == App.SupervisorChainStatus.NoSupervisor).ToArray();

        Assert.Equal(blocked ? 1 : 0, rejected.Length);
        Assert.All(rejected, branch => Assert.Single(branch.Hops));
        Assert.Equal(2, probe.Commands.Count);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
