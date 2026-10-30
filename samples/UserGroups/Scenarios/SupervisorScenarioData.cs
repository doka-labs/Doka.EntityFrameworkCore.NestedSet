namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>
/// Creates small independent application fixtures shared by the real console flows and their regression tests.
/// </summary>
internal static class SupervisorScenarioData
{
    private static readonly string[] s_userNames =
    [
        "X",
        "Anna",
        "Alex",
        "Ben",
        "Dana",
        "Carla",
        "Bea",
    ];

    /// <summary>Resolves the fixed fixture's display label without loading a user before or after a query.</summary>
    internal static string PersonLabel(
        SupervisorScenarioFixture fixture,
        int userId
    )
    {
        var index = Array.IndexOf(fixture.Users, userId);

        return index >= 0 && index < s_userNames.Length ? s_userNames[index] : $"User {userId}";
    }

    /// <summary>Resolves the fixture's structural group label without a database lookup.</summary>
    internal static string GroupLabel(
        SupervisorScenarioFixture fixture,
        int groupId
    )
    {
        var index = Array.IndexOf(fixture.Groups, groupId);

        return index < 0
            ? $"Group {groupId}"
            : index == 6
                ? "Independent team"
                : $"G{index}";
    }

    /// <summary>
    /// Creates a six-level management path, an independent team, explicit roles, and typed routing values.
    /// </summary>
    internal static async Task<SupervisorScenarioFixture> CreateAsync(
        UserGroupContext context,
        Guid tenantId,
        CancellationToken cancellationToken
    )
    {
        var role = Guid.NewGuid();
        await context.Roles.AddAsync(
            new Role
            {
                TenantId = tenantId,
                Id = role,
                Name = "Manager",
            },
            cancellationToken);

        await context.GroupTypes.AddRangeAsync(
            [
                new GroupType
                {
                    TenantId = tenantId,
                    Id = 1,
                    Name = "Manager",
                },
                new GroupType
                {
                    TenantId = tenantId,
                    Id = 2,
                    Name = "Supervisor",
                },
                new GroupType
                {
                    TenantId = tenantId,
                    Id = 3,
                    Name = "Foreman",
                },
                new GroupType
                {
                    TenantId = tenantId,
                    Id = 4,
                    Name = "User team",
                },
            ],
            cancellationToken);

        foreach (var field in new[] { "Route", "ManagerRoute", "ChannelClass", "RoutingZone" })
        {
            await context.UserMetadataDefinitions.AddAsync(
                new UserMetadataDefinition
                {
                    TenantId = tenantId,
                    FieldCode = field,
                },
                cancellationToken);

            foreach (var type in new[] { 1, 2, 3, 4 })
            {
                await context.MembershipMetadataDefinitions.AddAsync(
                    new MembershipMetadataDefinition
                    {
                        TenantId = tenantId,
                        GroupTypeId = type,
                        FieldCode = field,
                    },
                    cancellationToken);

                await context.GroupMetadataDefinitions.AddAsync(
                    new GroupMetadataDefinition
                    {
                        TenantId = tenantId,
                        GroupTypeId = type,
                        FieldCode = field,
                    },
                    cancellationToken);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        var tree = Guid.NewGuid();
        var groups = new UserGroup[7];
        var hierarchy = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        for (var index = 0; index < 6; index++)
        {
            var group = new UserGroup
            {
                Name = $"G{index}",
                TypeId = index switch
                {
                    3 => 2, 4 => 3, 5 => 4, _ => 1
                },
            };

            if (index == 0)
            {
                await hierarchy.InsertRootAsync(group, tree, cancellationToken);
            }
            else
            {
                await hierarchy.InsertChildAsync(group, groups[index - 1].Id, cancellationToken);
            }

            groups[index] = group;
        }

        groups[6] = new UserGroup
        {
            Name = "Independent team",
            TypeId = 4,
        };

        await hierarchy.InsertChildAsync(groups[6], groups[1].Id, cancellationToken);
        var people = new User[7];
        var groupIndexes = new[] { 5, 4, 4, 2, 1, 0, 6 };
        var routes = new[]
        {
            "lane-blue",
            "lane-blue",
            "lane-blue",
            "lane-blue",
            "lane-red",
            "lane-blue",
            "lane-blue",
        };

        // WHY: Store users at the same ordinals as the scenario names and subsequent relationship data.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < people.Length; index++)
        {
            var user = new User
            {
                Name = s_userNames[index],
                Position = index == 0 ? "Contributor" : "Team lead",
            };

            await context
                .NestedSet<User>()
                .ForScope(tenantId)
                .InsertRootAsync(user, Guid.NewGuid(), cancellationToken);

            people[index] = user;
        }

        // WHY: Facade mutations require a clean tracker; complete user inserts before staging domain links.
        // Keep users, group indexes and route values aligned by scenario ordinal.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < people.Length; index++)
        {
            var user = people[index];
            var group = groups[groupIndexes[index]];
            await context.UserGroupMemberships.AddAsync(
                new UserGroupMembership
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    UserGroupId = group.Id,
                    LeadershipPriority = index == 2 ? 20 : 10,
                },
                cancellationToken);

            await context.UserMetadatas.AddAsync(
                new UserMetadata
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    FieldCode = "Route",
                    Value = index == 1 ? "lane-red" : routes[index],
                },
                cancellationToken);

            await context.MembershipMetadatas.AddAsync(
                new MembershipMetadata
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    UserGroupId = group.Id,
                    GroupTypeId = group.TypeId!.Value,
                    FieldCode = "Route",
                    Value = routes[index],
                },
                cancellationToken);

            if (index > 0)
            {
                await context.MembershipRoles.AddAsync(
                    new MembershipRole
                    {
                        TenantId = tenantId,
                        UserId = user.Id,
                        UserGroupId = group.Id,
                        RoleId = role,
                    },
                    cancellationToken);
            }
        }

        await context.UserGroupMemberships.AddAsync(
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = people[0].Id,
                UserGroupId = groups[6].Id,
                LeadershipPriority = 50,
            },
            cancellationToken);

        await context.MembershipMetadatas.AddAsync(
            new MembershipMetadata
            {
                TenantId = tenantId,
                UserId = people[0].Id,
                UserGroupId = groups[6].Id,
                GroupTypeId = 4,
                FieldCode = "Route",
                Value = "lane-blue",
            },
            cancellationToken);

        await context.UserRoles.AddAsync(
            new UserRole
            {
                TenantId = tenantId,
                UserId = people[6].Id,
                RoleId = role,
            },
            cancellationToken);

        foreach (var group in groups)
        {
            if (group.ParentId is { } parent)
            {
                await context.GroupRelationPeriods.AddAsync(
                    new GroupRelationPeriod
                    {
                        TenantId = tenantId,
                        ParentId = parent,
                        ChildId = group.Id,
                        FromDate = new DateOnly(2026, 1, 1),
                    },
                    cancellationToken);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        return new SupervisorScenarioFixture(
            tenantId,
            tree,
            role,
            groups.Select(group => group.Id).ToArray(),
            people.Select(user => user.Id).ToArray());
    }
}

/// <summary>
/// Provides explicit application fixture keys without loading anything during candidate query construction.
/// </summary>
/// <param name="TenantId">The isolated fixture tenant.</param>
/// <param name="TreeId">The management tree.</param>
/// <param name="ManagerRoleId">The person-role definition.</param>
/// <param name="Groups">G0 through G5, followed by the independent team.</param>
/// <param name="Users">X, Anna, Alex, Ben, Dana, Carla, and Bea.</param>
internal sealed record SupervisorScenarioFixture(
    Guid TenantId,
    Guid TreeId,
    Guid ManagerRoleId,
    int[] Groups,
    int[] Users
)
{
    /// <summary>Creates an explicit nearest-eligible local-role search with user-to-membership routing.</summary>
    internal SupervisorRequest Request(
        SearchStart start = SearchStart.ParentsOnly,
        int? group = null
    ) => new()
    {
        SourceUserId = Users[0],
        StartGroupId = group,
        SearchStart = start,
        Roles = new MembershipRoleFilter(ManagerRoleId),
        Groups = new NthEligibleGroup([], 1),
        Matches = [new MetadataMatch(MetadataOwner.SourceUser, "Route", MetadataOwner.CandidateMembership, "Route")],
        OnDay = new DateOnly(2026, 10, 16),
    };
}
