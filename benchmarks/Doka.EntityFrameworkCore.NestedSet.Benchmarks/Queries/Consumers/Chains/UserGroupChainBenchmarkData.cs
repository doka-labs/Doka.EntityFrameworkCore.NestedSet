using Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Creates eight synthetic management levels and a separately shaped measured group population.</summary>
internal static class UserGroupChainBenchmarkData
{
    /// <summary>Seeds the actual sample model; every requested successful hop has a persisted manager level.</summary>
    internal static async Task<ChainBenchmarkFixture> CreateAsync(
        UserGroupContext context,
        int nodes,
        BenchmarkShape shape,
        BenchmarkChainProfile profile
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodes, 10);
        var (hops, fanout) = Dimensions(profile);
        var tenant = Guid.NewGuid();
        var role = Guid.NewGuid();
        await context.Roles.AddAsync(
            new Role
            {
                TenantId = tenant,
                Id = role,
                Name = "Measured manager",
            },
            CancellationToken.None);

        await context.GroupTypes.AddRangeAsync(
            [
                new GroupType
                {
                    TenantId = tenant,
                    Id = 1,
                    Name = "Management level",
                },
                new GroupType
                {
                    TenantId = tenant,
                    Id = 2,
                    Name = "Measured payload",
                },
            ],
            CancellationToken.None);

        await context.UserMetadataDefinitions.AddAsync(
            new UserMetadataDefinition
            {
                TenantId = tenant,
                FieldCode = "Route",
            },
            CancellationToken.None);

        foreach (var type in new[] { 1, 2 })
        {
            await context.MembershipMetadataDefinitions.AddAsync(
                new MembershipMetadataDefinition
                {
                    TenantId = tenant,
                    GroupTypeId = type,
                    FieldCode = "Route",
                },
                CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var groups = Enumerable
            .Range(0, nodes)
            .Select(index => new UserGroup
            {
                Name = FormattableString.Invariant($"Measured group {index:D9}"),
                TypeId = index < 9 ? 1 : 2,
            })
            .ToArray();

        var children = new List<int>?[nodes];

        for (var index = 1; index < nodes; index++)
        {
            var parent = index < 9
                ? index - 1
                : index == 9
                    ? 0
                    : shape switch
                    {
                        BenchmarkShape.Wide => 9,
                        BenchmarkShape.Deep => index - 1,
                        BenchmarkShape.Balanced => 9 + ((index - 10) / 2),
                        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
                    };

            (children[parent] ??= []).Add(index);
        }

        var branches = new NestedSetBranch<UserGroup>[nodes];

        for (var index = nodes - 1; index >= 0; index--)
        {
            branches[index] = new NestedSetBranch<UserGroup>(
                groups[index],
                children[index]
                    ?.Select(child => branches[child])
                    .ToArray());
        }

        var hierarchy = context
            .NestedSet<UserGroup>()
            .ForScope(tenant);

        await hierarchy.InsertRootAsync(groups[0], Guid.NewGuid(), CancellationToken.None);
        await hierarchy.InsertSubtreeAsync(branches[1], groups[0].Id, CancellationToken.None);
        await hierarchy.InsertSubtreeAsync(branches[9], groups[0].Id, CancellationToken.None);
        var users = new User[1 + (8 * fanout)];

        // WHY: Store inserted users in the stable slots used by the membership and routing fixture.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < users.Length; index++)
        {
            users[index] = new User { Name = FormattableString.Invariant($"Measured person {index:D3}") };
            await context
                .NestedSet<User>()
                .ForScope(tenant)
                .InsertRootAsync(users[index], Guid.NewGuid(), CancellationToken.None);
        }

        await AddMembershipAsync(
            context,
            tenant,
            users[0].Id,
            groups[8],
            role,
            false,
            "route-a");

        // WHY: Use the same user ordinal when assigning routing-switch fixture metadata.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < users.Length; index++)
        {
            await context.UserMetadatas.AddAsync(
                new UserMetadata
                {
                    TenantId = tenant,
                    UserId = users[index].Id,
                    FieldCode = "Route",
                    Value = profile == BenchmarkChainProfile.RoutingSwitch && index > 0 ? "route-b" : "route-a",
                },
                CancellationToken.None);
        }

        for (var level = 0; level < 8; level++)
        {
            for (var person = 0; person < fanout; person++)
            {
                var user = users[1 + (level * fanout) + person];
                var enabled = !(level == 6 && profile is BenchmarkChainProfile.Missing or BenchmarkChainProfile.Cycle);
                var route = profile == BenchmarkChainProfile.RoutingSwitch && person == 1 ? "route-b" : "route-a";
                await AddMembershipAsync(
                    context,
                    tenant,
                    user.Id,
                    groups[level],
                    role,
                    enabled,
                    route);
            }
        }

        if (profile == BenchmarkChainProfile.Cycle)
        {
            await AddMembershipAsync(
                context,
                tenant,
                users[0].Id,
                groups[6],
                role,
                true,
                "route-a");
        }

        if (profile == BenchmarkChainProfile.ReExpansion)
        {
            await AddMembershipAsync(
                context,
                tenant,
                users[1 + (7 * fanout)].Id,
                groups[9],
                role,
                false,
                "route-a");
        }

        foreach (var group in groups.Skip(1))
        {
            await context.GroupRelationPeriods.AddAsync(
                new GroupRelationPeriod
                {
                    TenantId = tenant,
                    ParentId = group.ParentId!.Value,
                    ChildId = group.Id,
                    FromDate = new DateOnly(2026, 1, 1),
                },
                CancellationToken.None);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        BenchmarkFixture.Require(
            await context.Groups.LongCountAsync(CancellationToken.None) == nodes,
            "The chain group's actual persisted population differs from Nodes.");

        var selection = profile is BenchmarkChainProfile.Ambiguous or BenchmarkChainProfile.RoutingSwitch
            ? SupervisorSelection.RequireUnique
            : SupervisorSelection.All;

        var step = new SupervisorStep(
            SearchStart.ParentsOnly,
            new MembershipRoleFilter(role),
            new NthTypeGroupThenValidate(1, 1),
            [new MetadataMatch(MetadataOwner.SourceUser, "Route", MetadataOwner.CandidateMembership, "Route")],
            new DateOnly(2026, 10, 16),
            selection);

        var request = new SupervisorChainRequest
        {
            SourceUserId = users[0].Id,
            StartGroupId = groups[8].Id,
            Steps = Enumerable
                .Repeat(step, hops)
                .ToArray(),
            RoutingOrigin = RoutingOrigin.InitialSubject,
            MaxFrontierStates = profile == BenchmarkChainProfile.FrontierLimit ? 1 : 1_024,
            MaxVisitedStates = 100_000,
            MaxResultRows = 1_000_000,
        };

        return new ChainBenchmarkFixture(
            tenant,
            request,
            users
                .Select(user => user.Id)
                .ToArray(),
            groups
                .Select(group => group.Id)
                .ToArray());
    }

    /// <summary>Maps finite named profiles to actual requested hops and candidate branching factors.</summary>
    internal static (int Hops, int Fanout) Dimensions(
        BenchmarkChainProfile profile
    ) => profile switch
    {
        BenchmarkChainProfile.Hops2Fanout1 => (2, 1),
        BenchmarkChainProfile.Hops4Fanout1 => (4, 1),
        BenchmarkChainProfile.Hops8Fanout1 => (8, 1),
        BenchmarkChainProfile.Hops2Fanout2 => (2, 2),
        BenchmarkChainProfile.Hops4Fanout2 => (4, 2),
        BenchmarkChainProfile.Hops8Fanout2 => (8, 2),
        BenchmarkChainProfile.Hops2Fanout4 => (2, 4),
        BenchmarkChainProfile.Hops4Fanout4 => (4, 4),
        BenchmarkChainProfile.Hops8Fanout4 => (8, 4),
        BenchmarkChainProfile.Missing or BenchmarkChainProfile.Cycle => (4, 1),
        BenchmarkChainProfile.Ambiguous
            or BenchmarkChainProfile.FrontierLimit
            or BenchmarkChainProfile.RoutingSwitch
            or BenchmarkChainProfile.ReExpansion => (4, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    /// <summary>Adds exact membership, local role, and typed routing values without inventing global grants.</summary>
    private static async Task AddMembershipAsync(
        UserGroupContext context,
        Guid tenant,
        int user,
        UserGroup group,
        Guid role,
        bool manager,
        string route
    )
    {
        await context.UserGroupMemberships.AddAsync(
            new UserGroupMembership
            {
                TenantId = tenant,
                UserId = user,
                UserGroupId = group.Id,
                LeadershipPriority = 10,
            },
            CancellationToken.None);

        await context.MembershipMetadatas.AddAsync(
            new MembershipMetadata
            {
                TenantId = tenant,
                UserId = user,
                UserGroupId = group.Id,
                GroupTypeId = group.TypeId!.Value,
                FieldCode = "Route",
                Value = route,
            },
            CancellationToken.None);

        if (manager)
        {
            await context.MembershipRoles.AddAsync(
                new MembershipRole
                {
                    TenantId = tenant,
                    UserId = user,
                    UserGroupId = group.Id,
                    RoleId = role,
                },
                CancellationToken.None);
        }
    }
}

/// <summary>Retains seeded identities solely for independent result and cursor assertions outside timing.</summary>
/// <param name="Tenant">The authorized isolated tenant.</param>
/// <param name="Request">The explicit finite chain and safety bounds.</param>
/// <param name="People">The source, then each level's measured managers.</param>
/// <param name="Groups">The nine management levels followed by the measured filler branch.</param>
internal sealed record ChainBenchmarkFixture(
    Guid Tenant,
    SupervisorChainRequest Request,
    int[] People,
    int[] Groups
);
