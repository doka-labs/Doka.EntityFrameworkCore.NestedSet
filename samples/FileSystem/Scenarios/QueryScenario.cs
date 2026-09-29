namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Shows tree isolation and composable hierarchy reads from a node identity alone.</summary>
internal static class QueryScenario
{
    /// <summary>Initializes its own forest and prints query results with their exact expected meaning.</summary>
    /// <param name="context">The scenario's unit of work.</param>
    /// <param name="details">Whether tree output includes structural columns.</param>
    /// <param name="cancellationToken">The token for queries, insertions, and output.</param>
    internal static async Task RunAsync(
        FileSystemContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("Queries: independent trees and anchors without a preload", cancellationToken);

        var forest = await FolderForest.CreateAsync(
            context,
            Guid.Parse("11111111-1111-1111-1111-111111111101"),
            Guid.Parse("11111111-1111-1111-1111-111111111102"),
            cancellationToken);

        var folders = context.NestedSet<Folder>();
        await FolderDisplay.TreeAsync(context, forest.TreeId, "Main tree", details, cancellationToken);
        await FolderDisplay.TreeAsync(context, forest.ArchiveTreeId, "Independent archive", details, cancellationToken);

        // WHY: The caller already has an identity; the SQL anchor join avoids an extra entity lookup.
        var tree = await folders
            .TreeContaining(forest.Repository.Id)
            .Select(folder => folder.Name)
            .ToListAsync(cancellationToken);

        var matchingIds = await folders
            .TreeContaining(forest.Repository.Id)
            .Where(folder => folder.Name == "NestedSet")
            .Select(folder => folder.Id)
            .ToListAsync(cancellationToken);

        var subtree = await folders
            .SubtreeOf(forest.Projects.Id)
            .Select(folder => folder.Name)
            .ToListAsync(cancellationToken);

        var children = await folders
            .ChildrenOf(forest.Root.Id)
            .Select(folder => folder.Name)
            .ToListAsync(cancellationToken);

        var parent = await folders
            .ParentOf(forest.Repository.Id)
            .Select(folder => folder.Name)
            .SingleAsync(cancellationToken);

        var nearestSystemAncestor = await folders
            .AncestorsOf(forest.Repository.Id)
            .Where(folder => folder.Category == "System")
            .OrderByDescending(folder => folder.Depth)
            .FirstOrDefaultAsync(cancellationToken);

        await SampleConsole.WriteLineAsync(
            $"Tree containing NestedSet: {string.Join(" -> ", tree)}",
            cancellationToken);

        await SampleConsole.WriteLineAsync($"Projects subtree: {string.Join(" -> ", subtree)}", cancellationToken);
        await SampleConsole.WriteLineAsync(
            $"System's direct children: {string.Join(", ", children)}",
            cancellationToken);

        await SampleConsole.WriteLineAsync($"NestedSet's parent: {parent}", cancellationToken);
        await SampleConsole.WriteLineAsync(
            $"Nearest ancestor with Category=System: {nearestSystemAncestor?.Name}",
            cancellationToken);

        SampleConsole.Require(
            tree.Count == 7,
            "TreeContaining must include every main-tree folder and no archive folder.");

        SampleConsole.Require(
            matchingIds.SequenceEqual([forest.Repository.Id]),
            "The filtered tree leaked the archive match.");

        SampleConsole.Require(
            subtree.SequenceEqual(["Projects", "NestedSet", "Notes"]),
            "The subtree order is incorrect.");

        SampleConsole.Require(children.SequenceEqual(["Documents", "Shared"]), "Sibling ordering is not alphabetic.");
        SampleConsole.Require(parent == "Projects", "ParentOf did not resolve the direct parent.");
        SampleConsole.Require(
            nearestSystemAncestor?.Id == forest.Projects.Id,
            "The closest matching ancestor is incorrect.");
    }
}
