namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Displays user reporting lines while reading structural shadow values in SQL.</summary>
internal static class SupervisorOutput
{
    /// <summary>Prints a scoped supervisor tree in hierarchy order, including application group membership.</summary>
    /// <param name="context">The context used for no-tracking reads.</param>
    /// <param name="tenantId">The selected tenant.</param>
    /// <param name="treeId">The selected supervisor tree.</param>
    /// <param name="heading">The heading for the displayed structure.</param>
    /// <param name="details">Whether to display persisted hierarchy coordinates.</param>
    /// <param name="cancellationToken">The token used for reads and output.</param>
    /// <returns>A task that completes when the tree has been printed.</returns>
    public static async Task TreeAsync(
        UserGroupContext context,
        Guid tenantId,
        Guid treeId,
        string heading,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(heading, cancellationToken);

        var tree = context
            .NestedSet<User>()
            .ForScope(tenantId)
            .InTree(treeId);

        // WHY: A membership join would omit users without groups and repeat users with several groups.
        // Read users once in preorder and aggregate the bounded membership rows in one separate query.
        /* Query syntax alternative:
        var users = await (from user in tree.Nodes
            orderby EF.Property<long>(user, UserHierarchyProperties.Left)
            select new
            {
                user.Id,
                user.Name,
                user.Position,
                user.SupervisorId,
                user.Depth,
                Left = EF.Property<long>(user, UserHierarchyProperties.Left),
                Right = EF.Property<long>(user, UserHierarchyProperties.Right),
                NestedSetPosition = EF.Property<long>(user, UserHierarchyProperties.NestedSetPosition),
            }).ToListAsync(cancellationToken);
        */

        var users = await tree
            .Nodes
            .OrderBy(user => EF.Property<long>(user, UserHierarchyProperties.Left))
            .Select(user => new
            {
                user.Id,
                user.Name,
                user.Position,
                user.SupervisorId,
                user.Depth,
                Left = EF.Property<long>(user, UserHierarchyProperties.Left),
                Right = EF.Property<long>(user, UserHierarchyProperties.Right),
                NestedSetPosition = EF.Property<long>(user, UserHierarchyProperties.NestedSetPosition),
            })
            .ToListAsync(cancellationToken);

        /* Query syntax alternative:
        var memberships = await (from membership in context.UserGroupMemberships
            join user in tree.Nodes on new
            {
                membership.TenantId,
                Id = membership.UserId,
            } equals new
            {
                user.TenantId,
                user.Id,
            }
            join membershipGroup in context.Groups on new
            {
                membership.TenantId,
                Id = membership.UserGroupId,
            } equals new
            {
                membershipGroup.TenantId,
                membershipGroup.Id,
            }
            orderby membershipGroup.Name, membershipGroup.Id
            select new
            {
                membership.UserId,
                GroupName = membershipGroup.Name,
            }).ToListAsync(cancellationToken);
        */

        var memberships = await context
            .UserGroupMemberships
            .Join(
                tree.Nodes,
                membership => new { membership.TenantId, Id = membership.UserId },
                user => new { user.TenantId, user.Id },
                (membership, user) => new { membership, user })
            .Join(
                context.Groups,
                pair => new { pair.membership.TenantId, Id = pair.membership.UserGroupId },
                membershipGroup => new { membershipGroup.TenantId, membershipGroup.Id },
                (pair, membershipGroup) => new { pair.membership, membershipGroup })
            .OrderBy(row => row.membershipGroup.Name)
            .ThenBy(row => row.membershipGroup.Id)
            .Select(row => new
            {
                row.membership.UserId,
                GroupName = row.membershipGroup.Name,
            })
            .ToListAsync(cancellationToken);

        var groupsByUser = memberships.ToLookup(membership => membership.UserId, membership => membership.GroupName);

        if (details)
        {
            await SampleConsole.WriteLineAsync($"TenantId={tenantId}; TreeId={treeId}", cancellationToken);
        }

        foreach (var user in users)
        {
            var groupNames = groupsByUser[user.Id];
            var groupLabel = groupsByUser.Contains(user.Id) ? string.Join(", ", groupNames) : "none";
            var line = new string(' ', user.Depth * 2) + $"{user.Name} - {user.Position} (groups: {groupLabel})";

            if (details)
            {
                line += $" [Id={user.Id}, SupervisorId={user.SupervisorId}, Depth={user.Depth}, "
                    + $"NestedSetPosition={user.NestedSetPosition}, Left={user.Left}, Right={user.Right}]";
            }

            await SampleConsole.WriteLineAsync(line, cancellationToken);
        }
    }

    /// <summary>Displays every stored supervisor tree during read-only sample inspection.</summary>
    /// <param name="context">The context opened for inspection.</param>
    /// <param name="details">Whether to display hierarchy coordinates.</param>
    /// <param name="cancellationToken">The token used for reads and output.</param>
    /// <returns>A task that completes after every root has been displayed.</returns>
    public static async Task InspectAsync(
        UserGroupContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        var roots = await context
            .Users
            .AsNoTracking()
            .Where(user => user.SupervisorId == null)
            .OrderBy(user => user.TenantId)
            .Select(user => new
            {
                user.TenantId,
                TreeId = EF.Property<Guid>(user, UserHierarchyProperties.TreeId),
                user.Name,
            })
            .ToListAsync(cancellationToken);

        foreach (var root in roots)
        {
            await TreeAsync(
                context,
                root.TenantId,
                root.TreeId,
                $"Stored supervisor tree: {root.Name}",
                details,
                cancellationToken);
        }
    }
}
