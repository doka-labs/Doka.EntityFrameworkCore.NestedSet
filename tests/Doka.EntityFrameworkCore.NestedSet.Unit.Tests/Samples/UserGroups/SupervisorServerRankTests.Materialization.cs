namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class SupervisorServerRankTests
{
    /// <summary>
    /// Server ranking ignores a repeated person's farther priority for Nth, retains its evidence, and keeps ties
    /// and unoccupied slots explicit. Highest deliberately uses the farther best-priority occurrence instead.
    /// </summary>
    /// <param name="highest">Whether business priority replaces Nth-person selection.</param>
    /// <param name="slot">The requested one-based distinct-person slot.</param>
    /// <param name="tie">Whether both nearest people occupy the same rank band.</param>
    /// <param name="expected">The expected explicit resolution status.</param>
    /// <param name="personIndex">The expected fixture user, or a negative unresolved sentinel.</param>
    /// <param name="evidenceCount">The number of retained original occurrences.</param>
    /// <param name="cursorGroupIndex">The canonical continuation group, or a negative unresolved sentinel.</param>
    [Theory]
    [InlineData(
        false,
        1,
        false,
        "Found",
        2,
        1,
        4)]
    [InlineData(
        false,
        2,
        false,
        "Found",
        1,
        2,
        4)]
    [InlineData(
        false,
        1,
        true,
        "Ambiguous",
        -1,
        0,
        -1)]
    [InlineData(
        false,
        2,
        true,
        "Ambiguous",
        -1,
        0,
        -1)]
    [InlineData(
        false,
        99,
        false,
        "NotFound",
        -1,
        0,
        -1)]
    [InlineData(
        false,
        int.MaxValue,
        false,
        "NotFound",
        -1,
        0,
        -1)]
    [InlineData(
        true,
        1,
        false,
        "Found",
        1,
        2,
        2)]
    public async Task ResolveAsync_ActualReader_PreservesNearestPersonRanksAndAllOriginalEvidence(
        bool highest,
        int slot,
        bool tie,
        string expected,
        int personIndex,
        int evidenceCount,
        int cursorGroupIndex
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var services = CreateServices(probe);
        await using var scope = services.CreateAsyncScope();
        await using var context = CreateContext(scope.ServiceProvider);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context
            .UserGroupMemberships
            .Where(member => member.TenantId == data.TenantId
                && member.UserId == data.Users[1])
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    member => member.LeadershipPriority,
                    tie
                        ? 0
                        : int.MaxValue),
                CancellationToken.None);

        await context
            .UserGroupMemberships
            .Where(member => member.TenantId == data.TenantId
                && member.UserId == data.Users[2])
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(member => member.LeadershipPriority, 0),
                CancellationToken.None);

        context.UserGroupMemberships.Add(
            new UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = data.Groups[2],
                LeadershipPriority = int.MinValue,
            });

        context.MembershipRoles.Add(
            new MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = data.Groups[2],
                RoleId = data.ManagerRoleId,
            });

        context.MembershipMetadatas.Add(
            new MembershipMetadata
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = data.Groups[2],
                GroupTypeId = 1,
                FieldCode = "Route",
                Value = "lane-blue",
            });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = data.Request(group: data.Groups[5]) with { Groups = new AllEligibleGroups() };
        var selection = highest
            ? SupervisorSelection.HighestBusinessPriority
            : SupervisorSelection.NthPerson(slot);

        probe.Enabled = true;

        // Act
        var result = await SupervisorQueries.ResolveAsync(
            context,
            data.TenantId,
            request,
            selection,
            CancellationToken.None);

        // Assert
        Assert.Equal(SupervisorBatchStatus.Completed, result.Status);
        var origin = Assert.Single(result.Origins);
        Assert.Equal(expected, origin.Status.ToString());
        Assert.Equal(
            personIndex < 0
                ? null
                : data.Users[personIndex],
            origin.Continuation?.CandidateUserId);

        Assert.Equal(
            cursorGroupIndex < 0
                ? null
                : data.Groups[cursorGroupIndex],
            origin.Continuation?.FoundGroupId);

        Assert.Equal(evidenceCount, origin.Evidence.Count);
        Assert.All(
            origin.Evidence,
            row =>
            {
                Assert.Equal(data.Users[personIndex], row.CandidateUserId);
                Assert.Equal(data.Groups[5], row.StartGroupId);
                Assert.Equal(data.TreeId, row.StartTreeId);
            });

        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A real zero rank remains occupied while a later absent slot cannot become rank zero.</summary>
    /// <param name="slot">The requested one-based person slot.</param>
    /// <param name="found">Whether that slot contains the sole qualifying person.</param>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task ResolveAsync_ValidRankZero_IsDistinctFromAnUnoccupiedSlot(
        int slot,
        bool found
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var services = CreateServices(probe);
        await using var scope = services.CreateAsyncScope();
        await using var context = CreateContext(scope.ServiceProvider);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        context.UserGroupMemberships.Add(
            new UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = data.Groups[5],
                LeadershipPriority = int.MinValue,
            });

        context.MembershipRoles.Add(
            new MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = data.Groups[5],
                RoleId = data.ManagerRoleId,
            });

        context.MembershipMetadatas.Add(
            new MembershipMetadata
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = data.Groups[5],
                GroupTypeId = 4,
                FieldCode = "Route",
                Value = "lane-blue",
            });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = data.Request(SearchStart.IncludeSelf, data.Groups[5]) with
        {
            Groups = new AllEligibleGroups(4),
        };

        probe.Enabled = true;

        // Act
        var result = await SupervisorQueries.ResolveAsync(
            context,
            data.TenantId,
            request,
            SupervisorSelection.NthPerson(slot),
            CancellationToken.None);

        // Assert
        var origin = Assert.Single(result.Origins);
        Assert.Equal(
            found
                ? SupervisorResolutionStatus.Found
                : SupervisorResolutionStatus.NotFound,
            origin.Status);

        Assert.Equal(
            found
                ? data.Users[1]
                : null,
            origin.Continuation?.CandidateUserId);

        Assert.Equal(
            found
                ? 1
                : 0,
            origin.Evidence.Count);

        Assert.All(
            origin.Evidence,
            row =>
            {
                Assert.Equal(0, row.Distance);
                Assert.Equal(int.MinValue, row.LeadershipPriority);
            });

        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
