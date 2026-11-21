namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Displays small group hierarchies and their application-defined grants.</summary>
internal static class GroupOutput
{
    /// <summary>Prints one complete tree in hierarchy preorder with optional structural detail.</summary>
    /// <param name="context">The context used for no-tracking hierarchy reads.</param>
    /// <param name="tenantId">The tenant partition.</param>
    /// <param name="treeId">The selected stable tree identity.</param>
    /// <param name="heading">The readable label identifying the displayed state.</param>
    /// <param name="details">Whether to include persisted identity and coordinate columns.</param>
    /// <param name="output">The writer owned by this invocation.</param>
    /// <param name="cancellationToken">The token used for reading and output.</param>
    /// <returns>A task that completes after the tree has been displayed.</returns>
    public static async Task TreeAsync(
        UserGroupContext context,
        Guid tenantId,
        Guid treeId,
        string heading,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(output, heading, cancellationToken);

        var groups = await context
            .NestedSet<UserGroup>()
            .ForScope(tenantId)
            .InTree(treeId)
            .Nodes
            .ToListAsync(cancellationToken);

        if (details)
        {
            await SampleConsole.WriteLineAsync(output, $"TenantId={tenantId}; TreeId={treeId}", cancellationToken);
        }

        // WHY: The completed input list is stable; preserve its order while updating separate result state.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];

            var text = new string(' ', group.Depth * 2) + group.Name;

            if (details)
            {
                text += $" [Id={group.Id}, ParentId={group.ParentId}, Depth={group.Depth}, "
                    + $"Position={group.Position}, Left={group.Left}, Right={group.Right}]";
            }

            await SampleConsole.WriteLineAsync(output, text, cancellationToken);
        }
    }

    /// <summary>Displays the effective grant query for one selected group.</summary>
    /// <param name="context">The application context.</param>
    /// <param name="tenantId">The tenant partition.</param>
    /// <param name="groupId">The group receiving the grants.</param>
    /// <param name="label">The name displayed beside the codes.</param>
    /// <param name="output">The writer owned by this invocation.</param>
    /// <param name="cancellationToken">The token used for querying and output.</param>
    /// <returns>The displayed permission codes for the scenario's precise expectations.</returns>
    public static async Task<List<string>> PrivilegesAsync(
        UserGroupContext context,
        Guid tenantId,
        int groupId,
        string label,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var privileges = await PrivilegeQueries.EffectiveAsync(context, tenantId, groupId, cancellationToken);

        await SampleConsole.WriteLineAsync(output, $"{label}: {string.Join(", ", privileges)}", cancellationToken);

        return privileges;
    }

    /// <summary>Inspects existing trees and direct assignments without migrating or changing the database.</summary>
    /// <param name="context">The context used exclusively for inspection.</param>
    /// <param name="details">Whether tree output includes structural columns.</param>
    /// <param name="output">The writer owned by this invocation.</param>
    /// <param name="cancellationToken">The token used for all reads and output.</param>
    /// <returns>A task that completes after stored results have been printed.</returns>
    public static async Task InspectAsync(
        UserGroupContext context,
        bool details,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var roots = await context
            .Groups
            .AsNoTracking()
            .Where(group => group.ParentId == null)
            .OrderBy(group => group.TenantId)
            .Select(group => new
            {
                group.Id,
                group.TenantId,
                group.TreeId,
                group.Name,
            })
            .ToListAsync(cancellationToken);

        // WHY: The completed input list is stable; preserve its order while updating separate result state.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            var root = roots[rootIndex];

            await TreeAsync(
                context,
                root.TenantId,
                root.TreeId,
                $"Stored tree: {root.Name}",
                details,
                output,
                cancellationToken);
        }

        // WHY: Keep the equivalent query form in this sample so either LINQ style can be tried unchanged.
        /* Query syntax alternative:
        var assignments = await (from assignment in context.GroupRoles
            join node in context.Groups on new
            {
                assignment.TenantId,
                Id = assignment.UserGroupId,
            } equals new
            {
                node.TenantId,
                node.Id,
            }
            join role in context.Roles on new
            {
                assignment.TenantId,
                Id = assignment.RoleId,
            } equals new
            {
                role.TenantId,
                role.Id,
            }
            join grant in context.RolePrivileges on new
            {
                role.TenantId,
                RoleId = role.Id,
            } equals new
            {
                grant.TenantId,
                grant.RoleId,
            }
            orderby node.Name, role.Name, grant.PrivilegeCode
            select new
            {
                node.TenantId,
                Group = node.Name,
                Role = role.Name,
                grant.PrivilegeCode,
            }).ToListAsync(cancellationToken);
        */

        var assignments = await context
            .GroupRoles
            .Join(
                context.Groups,
                assignment => new
                {
                    assignment.TenantId,
                    Id = assignment.UserGroupId,
                },
                node => new
                {
                    node.TenantId,
                    node.Id,
                },
                (assignment, node) => new
                {
                    assignment,
                    node,
                })
            .Join(
                context.Roles,
                pair => new
                {
                    pair.assignment.TenantId,
                    Id = pair.assignment.RoleId,
                },
                role => new
                {
                    role.TenantId,
                    role.Id,
                },
                (pair, role) => new
                {
                    pair.node,
                    role,
                })
            .Join(
                context.RolePrivileges,
                pair => new
                {
                    pair.role.TenantId,
                    RoleId = pair.role.Id,
                },
                grant => new
                {
                    grant.TenantId,
                    grant.RoleId,
                },
                (pair, grant) => new
                {
                    pair.node,
                    pair.role,
                    grant,
                })
            .OrderBy(row => row.node.Name)
            .ThenBy(row => row.role.Name)
            .ThenBy(row => row.grant.PrivilegeCode)
            .Select(row => new
            {
                row.node.TenantId,
                Group = row.node.Name,
                Role = row.role.Name,
                row.grant.PrivilegeCode,
            })
            .ToListAsync(cancellationToken);

        await SampleConsole.HeadingAsync(output, "Stored direct role assignments:", cancellationToken);

        // WHY: The completed input list is stable; preserve its order while updating separate result state.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var assignmentIndex = 0; assignmentIndex < assignments.Count; assignmentIndex++)
        {
            var assignment = assignments[assignmentIndex];

            await SampleConsole.WriteLineAsync(
                output,
                $"{assignment.Group} -> {assignment.Role} -> {assignment.PrivilegeCode} "
                + $"(TenantId={assignment.TenantId})",
                cancellationToken);
        }

        await SupervisorOutput.InspectAsync(context, details, output, cancellationToken);

        await SampleConsole.WriteLineAsync(
            output,
            "Inspection completed without changing stored results.",
            cancellationToken);
    }
}
