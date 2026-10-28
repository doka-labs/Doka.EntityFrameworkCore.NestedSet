using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Measures the real frontier transport and person fan-out at its qualified state boundary.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Six distinct current subjects require two bounded second-hop batches, with real one/four/sixteen-field
    /// predicates (U59).
    /// </summary>
    /// <param name="fields">The number of routing metadata criteria carried by every physical state.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    public async Task FrontierBatchesFollowPhysicalStatesInsteadOfLogicalPeopleQueries(int fields)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await SeedOriginProfileAsync(context, data, 1, fields);
        var leaf = await context.UserGroupMemberships
            .Where(member => member.TenantId == data.TenantId && member.UserId == data.Users[0])
            .Select(member => member.UserGroupId)
            .SingleAsync(CancellationToken.None);

        foreach (var index in new[] { 2, 3, 4, 5, 6 })
        {
            await context.UserGroupMemberships.AddAsync(new App.UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[index],
                UserGroupId = leaf,
                LeadershipPriority = index,
            }, CancellationToken.None);
            await context.MembershipRoles.AddAsync(new App.MembershipRole
            {
                TenantId = data.TenantId,
                UserId = data.Users[index],
                UserGroupId = leaf,
                RoleId = data.ManagerRoleId,
            }, CancellationToken.None);
            foreach (var field in s_profileFields.Take(fields))
            {
                await context.MembershipMetadatas.AddAsync(new App.MembershipMetadata
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[index],
                    UserGroupId = leaf,
                    GroupTypeId = 4,
                    FieldCode = field,
                    Value = "lane-blue",
                }, CancellationToken.None);
            }
        }

        for (var index = 1; index < fields; index++)
        {
            var field = s_profileFields[index];
            if (index >= 4)
            {
                await context.MembershipMetadataDefinitions.AddAsync(new App.MembershipMetadataDefinition
                {
                    TenantId = data.TenantId,
                    GroupTypeId = 1,
                    FieldCode = field,
                }, CancellationToken.None);
            }

            await context.MembershipMetadatas.AddAsync(new App.MembershipMetadata
            {
                TenantId = data.TenantId,
                UserId = data.Users[5],
                UserGroupId = data.Groups[0],
                GroupTypeId = 1,
                FieldCode = field,
                Value = "lane-blue",
            }, CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await EnableSnapshotAsync(context);
        var matches = s_profileFields.Take(fields).Select(field => new App.MetadataMatch(
            App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray();

        var request = new App.SupervisorChainRequest
        {
            SourceUserId = data.Users[0],
            StartGroupId = leaf,
            RoutingOrigin = App.RoutingOrigin.InitialSubject,
            MaxFrontierStates = 64,
            MaxVisitedStates = 256,
            MaxResultRows = 512,
            Steps =
            [
                new(App.SearchStart.IncludeSelf, new App.MembershipRoleFilter(data.ManagerRoleId),
                    new App.NthTypeGroupThenValidate(4, 1), matches, null, App.SupervisorSelection.All),
                new(App.SearchStart.ParentsOnly, new App.MembershipRoleFilter(data.ManagerRoleId),
                    new App.NthTypeGroupThenValidate(1, 1), matches, null, App.SupervisorSelection.RequireUnique),
            ],
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.FollowOriginalPathAsync(
            context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.True(result.Complete);
        Assert.Equal(6, result.Branches.Count);
        Assert.Equal(
            5, result.Branches.Count(branch => branch.Status == App.SupervisorChainStatus.ReachedRequestedHop));
        Assert.Equal(1, result.Branches.Count(branch => branch.Status == App.SupervisorChainStatus.NoSupervisor));
        Assert.Equal(3, probe.Commands.Count);
        Assert.All(probe.ParameterCounts, count => Assert.InRange(count, 1, 1800));
        Assert.All(probe.Commands, sql => Assert.InRange(sql.Length, 1, 2_000_000));
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// OriginalPath and AllMemberships make different explicit choices when a selected person owns another tree
    /// (U40).
    /// </summary>
    /// <param name="expanded">Whether the second hop searches the selected supervisor's other own tree.</param>
    /// <param name="branches">The expected number of separately retained management paths.</param>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task ReExpansionCanIntroduceAnOwnTreeWithoutChangingTenant(
        bool expanded,
        int branches
    )
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var secondTree = Guid.NewGuid();
        var hierarchy = context.NestedSet<App.UserGroup>().ForScope(data.TenantId);
        var root = new App.UserGroup
        {
            Name = "Other own tree manager",
            TypeId = 1,
        };

        var child = new App.UserGroup
        {
            Name = "Other own membership",
            TypeId = 4,
        };

        await hierarchy.InsertRootAsync(root, secondTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(child, root.Id, CancellationToken.None);
        await context.UserGroupMemberships.AddRangeAsync([
            new App.UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[1],
                UserGroupId = child.Id,
                LeadershipPriority = 10,
            },
            new App.UserGroupMembership
            {
                TenantId = data.TenantId,
                UserId = data.Users[6],
                UserGroupId = root.Id,
                LeadershipPriority = 10,
            }], CancellationToken.None);
        await context.MembershipRoles.AddAsync(new App.MembershipRole
        {
            TenantId = data.TenantId,
            UserId = data.Users[6],
            UserGroupId = root.Id,
            RoleId = data.ManagerRoleId,
        }, CancellationToken.None);
        await context.MembershipMetadatas.AddAsync(new App.MembershipMetadata
        {
            TenantId = data.TenantId,
            UserId = data.Users[6],
            UserGroupId = root.Id,
            GroupTypeId = 1,
            FieldCode = "Route",
            Value = "lane-blue",
        }, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await EnableSnapshotAsync(context);
        var original = ChainRequest(data, false);
        var steps = original.Steps.Select(step => step with { OnDay = null }).ToArray();
        var request = original with { Steps = steps };
        probe.Enabled = true;

        // Act
        var result = expanded
            ? await App.SupervisorQueries.FollowAllMembershipsAsync(
                context, data.TenantId, request, CancellationToken.None)
            : await App.SupervisorQueries.FollowOriginalPathAsync(
                context, data.TenantId, request, CancellationToken.None);

        // Assert
        Assert.True(result.Complete);
        Assert.Equal(branches, result.Branches.Count);
        Assert.All(
            result.Branches.SelectMany(branch => branch.Hops),
            hop => Assert.Equal(data.TenantId, hop.Evidence.TenantId));
        Assert.Equal(expanded ? new[] { data.TreeId, secondTree }.Order() : new[] { data.TreeId },
            result.Branches.Select(branch => branch.Hops[^1].Evidence.StartTreeId).Order());
        Assert.All(result.Branches, branch => Assert.Equal(data.TreeId, branch.Hops[^1].InitialTreeId));
        Assert.Equal(2, probe.Commands.Count);
    }
}
