using GroupApp = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal sealed partial record CompositionConsumerDataset
{

    /// <summary>Seeds four source populations, one roleholder, and sixteen real scalar routing fields.</summary>
    private async Task SeedGroupsAsync(
        CompositionCapacityGroupContext context,
        CancellationToken cancellationToken
    )
    {
        var tenant = CompositionConsumerModel.Tenant;
        context.GroupTypes.Add(
            new GroupApp.GroupType
            {
                TenantId = tenant,
                Id = 1,
                Name = "Capacity",
            });

        context.Roles.Add(
            new GroupApp.Role
            {
                TenantId = tenant,
                Id = ManagerRole,
                Name = "Manager",
            });

        for (var hop = 0; hop < 8; hop++)
        {
            context.Roles.Add(
                new GroupApp.Role
                {
                    TenantId = tenant,
                    Id = CompositionConsumerModel.Identity(201 + hop),
                    Name = "Chain manager",
                });
        }

        foreach (var key in OriginCounts
            .Select(SourceUser)
            .Prepend(2)
            .Concat(Enumerable.Range(200, 8)))
        {
            var user = new GroupApp.User
            {
                Id = key,
                TenantId = tenant,
                Name = "Capacity user",
                Position = "User",
            };

            context.Users.Add(user);
            context
                    .Entry(user)
                    .Property<Guid>(GroupApp.UserHierarchyProperties.TreeId)
                    .CurrentValue
                = CompositionConsumerModel.Identity(key + 1_000_000);

            context
                .Entry(user)
                .Property<long>(GroupApp.UserHierarchyProperties.Left)
                .CurrentValue = 1;

            context
                .Entry(user)
                .Property<long>(GroupApp.UserHierarchyProperties.Right)
                .CurrentValue = 2;
        }

        // WHY: These few independent user roots are unrelated to the measured group hierarchy. Fixture seeding
        // supplies canonical coordinates once; the consumer's actual query/save code remains unchanged.
        await context.SavePrecomputedHierarchyAsync(cancellationToken);

        context.UserGroupMemberships.Add(
            new GroupApp.UserGroupMembership
            {
                TenantId = tenant,
                UserId = 2,
                UserGroupId = 1,
                LeadershipPriority = 1,
            });

        context.MembershipRoles.Add(
            new GroupApp.MembershipRole
            {
                TenantId = tenant,
                UserId = 2,
                UserGroupId = 1,
                RoleId = ManagerRole,
            });

        context.UserRoles.Add(
            new GroupApp.UserRole
            {
                TenantId = tenant,
                UserId = 2,
                RoleId = ManagerRole,
            });

        for (var hop = 0; hop < 8; hop++)
        {
            var group = Nodes - hop - 1;
            context.UserGroupMemberships.AddRange(
                new GroupApp.UserGroupMembership
                {
                    TenantId = tenant,
                    UserId = 200 + hop,
                    UserGroupId = group,
                    LeadershipPriority = 1,
                },
                new GroupApp.UserGroupMembership
                {
                    TenantId = tenant,
                    UserId = 200 + hop,
                    UserGroupId = Nodes,
                    LeadershipPriority = 1,
                });

            context.MembershipRoles.Add(
                new GroupApp.MembershipRole
                {
                    TenantId = tenant,
                    UserId = 200 + hop,
                    UserGroupId = group,
                    RoleId = CompositionConsumerModel.Identity(201 + hop),
                });
        }

        foreach (var count in OriginCounts)
        {
            for (var offset = 0; offset < count; offset++)
            {
                context.UserGroupMemberships.Add(
                    new GroupApp.UserGroupMembership
                    {
                        TenantId = tenant,
                        UserId = SourceUser(count),
                        UserGroupId = Nodes - offset,
                    });
            }
        }

        foreach (var field in GroupFields)
        {
            context.UserMetadataDefinitions.Add(
                new GroupApp.UserMetadataDefinition
                {
                    TenantId = tenant,
                    FieldCode = field,
                });

            context.MembershipMetadataDefinitions.Add(
                new GroupApp.MembershipMetadataDefinition
                {
                    TenantId = tenant,
                    GroupTypeId = 1,
                    FieldCode = field,
                });

            context.MembershipMetadatas.Add(
                new GroupApp.MembershipMetadata
                {
                    TenantId = tenant,
                    GroupTypeId = 1,
                    UserId = 2,
                    UserGroupId = 1,
                    FieldCode = field,
                    Value = "lane-blue",
                });

            foreach (var count in OriginCounts)
            {
                context.UserMetadatas.Add(
                    new GroupApp.UserMetadata
                    {
                        TenantId = tenant,
                        UserId = SourceUser(count),
                        FieldCode = field,
                        Value = "lane-blue",
                    });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }
}
