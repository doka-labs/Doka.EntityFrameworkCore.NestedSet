namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Shows complete subtree moves within a tree, across trees, and into a new independent tree.</summary>
internal static class MoveScenario
{
    /// <summary>Moves the Projects branch while checking its parent, membership, and normalized depth.</summary>
    /// <param name="context">The scenario's unit of work.</param>
    /// <param name="details">Whether tree output includes structural columns.</param>
    /// <param name="cancellationToken">The token for all asynchronous work.</param>
    internal static async Task RunAsync(
        FileSystemContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("Move: within a tree, into another tree, and detach", cancellationToken);

        var forest = await FolderForest.CreateAsync(
            context,
            Guid.Parse("33333333-3333-3333-3333-333333333301"),
            Guid.Parse("33333333-3333-3333-3333-333333333302"),
            cancellationToken);

        var folders = context.NestedSet<Folder>();
        await FolderDisplay.TreeAsync(context, forest.TreeId, "Before moving Projects", details, cancellationToken);

        await folders.MoveToAsync(forest.Projects.Id, forest.Shared.Id, cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            forest.TreeId,
            "Projects moved beneath Shared",
            details,
            cancellationToken);

        await FolderDisplay.RequireNamesAsync(
            context,
            forest.TreeId,
            [
                "System",
                "Documents",
                "Shared",
                "Projects",
                "NestedSet",
                "Notes",
                "Public"
            ],
            "The same-tree move did not move the complete Projects subtree.",
            cancellationToken);

        await folders.MoveToAsync(forest.Projects.Id, forest.ArchiveRoot.Id, cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            forest.TreeId,
            "Source after cross-tree move",
            details,
            cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            forest.ArchiveTreeId,
            "Archive after cross-tree move",
            details,
            cancellationToken);

        await FolderDisplay.RequireNamesAsync(
            context,
            forest.ArchiveTreeId,
            [
                "Archive",
                "NestedSet",
                "Projects",
                "NestedSet",
                "Notes",
            ],
            "The cross-tree move did not transfer the complete Projects subtree.",
            cancellationToken);

        SampleConsole.Require(
            !await folders
                .InTree(forest.TreeId)
                .Nodes
                .AnyAsync(folder => folder.Id == forest.Repository.Id, cancellationToken),
            "The moved repository is still a member of the source tree.");

        var detachedTreeId = Guid.Parse("33333333-3333-3333-3333-333333333303");
        await folders.DetachAsTreeAsync(forest.Projects.Id, detachedTreeId, cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            detachedTreeId,
            "Projects detached as an independent tree",
            details,
            cancellationToken);

        // WHY: Structural writes are set-based. Query the final node instead of reading the original detached object.
        var root = await folders
            .InTree(detachedTreeId)
            .Nodes
            .SingleAsync(folder => folder.Id == forest.Projects.Id, cancellationToken);

        SampleConsole.Require(root.ParentId is null, "The detached root still has a parent.");
        SampleConsole.Require(root is { Depth: 0, Position: 0 }, "The detached root was not normalized.");
        await FolderDisplay.RequireNamesAsync(
            context,
            detachedTreeId,
            ["Projects", "NestedSet", "Notes"],
            "Detaching lost a descendant or changed sibling order.",
            cancellationToken);

        await FolderDisplay.RequireNamesAsync(
            context,
            forest.ArchiveTreeId,
            ["Archive", "NestedSet"],
            "Detaching changed the unrelated archive branch.",
            cancellationToken);
    }
}
