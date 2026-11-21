using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Builds medium application data around the existing small hierarchy, preserving tenant-qualified foreign keys.
    /// </summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="context">The context that persists the profile data.</param>
    /// <param name="data">The existing hierarchy and users extended by the profile.</param>
    /// <param name="origins">The number of new own-membership origins.</param>
    /// <param name="fields">The number of matching routing fields on each origin.</param>
    private async Task SeedOriginProfileAsync(
        App.UserGroupContext context,
        App.SupervisorScenarioFixture data,
        int origins,
        int fields,
        CancellationToken cancellationToken
    )
    {
        await context
            .UserGroupMemberships
            .Where(member => member.TenantId == data.TenantId && member.UserId == data.Users[0])
            .ExecuteDeleteAsync(cancellationToken);

        var root = await context
            .Groups
            .Where(group => group.TenantId == data.TenantId && group.Id == data.Groups[0])
            .Select(group => new
            {
                group.Right,
                group.Depth,
            })
            .SingleAsync(cancellationToken);

        await context
            .Groups
            .Where(group => group.TenantId == data.TenantId && group.Id == data.Groups[0])
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(group => group.Right, root.Right + origins * 2L),
                cancellationToken);

        var groups = new App.UserGroup[origins];
        for (var index = 0; index < origins; index++)
        {
            groups[index] = new App.UserGroup
            {
                TenantId = data.TenantId,
                TreeId = data.TreeId,
                Name = $"Profile origin {index}",
                TypeId = 4,
                ParentId = data.Groups[0],
                Left = root.Right + index * 2L,
                Right = root.Right + index * 2L + 1,
                Depth = 1,
                Position = index + 1,
            };
        }

        await context.Groups.AddRangeAsync(groups, cancellationToken);
        await context.SavePrecomputedHierarchyAsync(cancellationToken);
        for (var fieldIndex = 0; fieldIndex < fields; fieldIndex++)
        {
            var field = s_profileFields[fieldIndex];
            if (fieldIndex >= 4)
            {
                await context.UserMetadataDefinitions.AddAsync(
                    new App.UserMetadataDefinition
                    {
                        TenantId = data.TenantId,
                        FieldCode = field,
                    },
                    cancellationToken);

                await context.MembershipMetadataDefinitions.AddAsync(
                    new App.MembershipMetadataDefinition
                    {
                        TenantId = data.TenantId,
                        GroupTypeId = 4,
                        FieldCode = field,
                    },
                    cancellationToken);
            }

            if (fieldIndex != 0)
            {
                await context.UserMetadatas.AddAsync(
                    new App.UserMetadata
                    {
                        TenantId = data.TenantId,
                        UserId = data.Users[0],
                        FieldCode = field,
                        Value = "lane-blue",
                    },
                    cancellationToken);
            }
        }

        foreach (var group in groups)
        {
            await context.UserGroupMemberships.AddRangeAsync(
                [
                    new App.UserGroupMembership
                    {
                        TenantId = data.TenantId,
                        UserId = data.Users[0],
                        UserGroupId = group.Id,
                        LeadershipPriority = 50,
                    },
                    new App.UserGroupMembership
                    {
                        TenantId = data.TenantId,
                        UserId = data.Users[1],
                        UserGroupId = group.Id,
                        LeadershipPriority = 10,
                    },
                ],
                cancellationToken);

            await context.MembershipRoles.AddAsync(
                new App.MembershipRole
                {
                    TenantId = data.TenantId,
                    UserId = data.Users[1],
                    UserGroupId = group.Id,
                    RoleId = data.ManagerRoleId,
                },
                cancellationToken);

            foreach (var field in s_profileFields.Take(fields))
            {
                await context.MembershipMetadatas.AddAsync(
                    new App.MembershipMetadata
                    {
                        TenantId = data.TenantId,
                        UserId = data.Users[1],
                        UserGroupId = group.Id,
                        GroupTypeId = 4,
                        FieldCode = field,
                        Value = "lane-blue",
                    },
                    cancellationToken);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        await PrepareConsumerStatisticsAsync(context, cancellationToken: cancellationToken);
    }
}
