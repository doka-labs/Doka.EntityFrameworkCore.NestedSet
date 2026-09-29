namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Prints the deliberately small sample trees without changing their persisted hierarchy order.</summary>
internal static class FolderDisplay
{
    /// <summary>Prints one tree in preorder and optionally exposes its structural columns.</summary>
    /// <param name="context">The unit of work used only for this read.</param>
    /// <param name="treeId">The exact tree identity to display.</param>
    /// <param name="heading">The human-readable state label.</param>
    /// <param name="details">Whether to display identities, depth, position, and bounds.</param>
    /// <param name="cancellationToken">The token for the query and output.</param>
    internal static async Task TreeAsync(
        FileSystemContext context,
        Guid treeId,
        string heading,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.WriteLineAsync($"{heading} (TreeId={treeId})", cancellationToken);

        // WHY: Name orders siblings in the model. Replacing the query's Left order would mix hierarchy levels.
        var nodes = await context
            .NestedSet<Folder>()
            .InTree(treeId)
            .Nodes
            .ToListAsync(cancellationToken);

        if (nodes.Count == 0)
        {
            await SampleConsole.WriteLineAsync("  (no folders)", cancellationToken);
        }

        foreach (var folder in nodes)
        {
            var detail = details
                ? $" [Id={folder.Id}, ParentId={folder.ParentId?.ToString(CultureInfo.InvariantCulture) ?? "root"}, "
                + $"Depth={folder.Depth}, Position={folder.Position}, Bounds={folder.Left}..{folder.Right}]"
                : string.Empty;

            await SampleConsole.WriteLineAsync(
                $"{new string(' ', folder.Depth * 2)}{folder.Name}{detail}",
                cancellationToken);
        }
    }

    /// <summary>Inspects existing roots and trees without seeding, migrating, or updating them.</summary>
    /// <param name="context">The read-only inspection unit of work.</param>
    /// <param name="details">Whether to include structural details.</param>
    /// <param name="cancellationToken">The token for all reads and output.</param>
    internal static async Task StoredTreesAsync(
        FileSystemContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        var treeIds = await context
            .Folders
            .AsNoTracking()
            .Where(folder => folder.ParentId == null)
            .OrderBy(folder => folder.Name)
            .ThenBy(folder => folder.TreeId)
            .Select(folder => folder.TreeId)
            .ToListAsync(cancellationToken);

        if (treeIds.Count == 0)
        {
            await SampleConsole.WriteLineAsync("No active folder trees are stored.", cancellationToken);
        }

        foreach (var treeId in treeIds)
        {
            await TreeAsync(context, treeId, "Stored tree", details, cancellationToken);
        }
    }

    /// <summary>Checks the expected tree preorder with a specific explanation on failure.</summary>
    /// <param name="context">The unit of work for this query.</param>
    /// <param name="treeId">The complete tree to compare.</param>
    /// <param name="expected">The expected names in hierarchy preorder.</param>
    /// <param name="message">The violated scenario contract.</param>
    /// <param name="cancellationToken">The token for the query.</param>
    internal static async Task RequireNamesAsync(
        FileSystemContext context,
        Guid treeId,
        IReadOnlyList<string> expected,
        string message,
        CancellationToken cancellationToken
    )
    {
        var names = await context
            .NestedSet<Folder>()
            .InTree(treeId)
            .Nodes
            .Select(folder => folder.Name)
            .ToListAsync(cancellationToken);

        SampleConsole.Require(names.SequenceEqual(expected), message);
    }
}
