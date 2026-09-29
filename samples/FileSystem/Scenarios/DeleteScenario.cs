namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Distinguishes node deletion with child promotion, subtree deletion, and whole-tree deletion.</summary>
internal static class DeleteScenario
{
    /// <summary>Executes the three deletion contracts against its own demonstration forest.</summary>
    /// <param name="context">The scenario's unit of work.</param>
    /// <param name="details">Whether output includes structural details.</param>
    /// <param name="cancellationToken">The token for all asynchronous work.</param>
    internal static async Task RunAsync(
        FileSystemContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("Delete: one folder, a complete branch, or an entire tree", cancellationToken);

        var forest = await FolderForest.CreateAsync(
            context,
            Guid.Parse("44444444-4444-4444-4444-444444444401"),
            Guid.Parse("44444444-4444-4444-4444-444444444402"),
            cancellationToken);

        var folders = context.NestedSet<Folder>();
        await FolderDisplay.TreeAsync(context, forest.TreeId, "Before deletion", details, cancellationToken);

        await folders.DeleteAsync(forest.Projects.Id, cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            forest.TreeId,
            "DeleteAsync(Projects): children promoted",
            details,
            cancellationToken);

        var promoted = await folders
            .ParentOf(forest.Repository.Id)
            .Select(folder => folder.Id)
            .SingleAsync(cancellationToken);

        SampleConsole.Require(promoted == forest.Documents.Id, "DeleteAsync did not promote the direct children.");
        await FolderDisplay.RequireNamesAsync(
            context,
            forest.TreeId,
            [
                "System",
                "Documents",
                "NestedSet",
                "Notes",
                "Shared",
                "Public",
            ],
            "Node deletion removed children or left the deleted folder visible.",
            cancellationToken);

        await folders.DeleteSubtreeAsync(forest.Documents.Id, cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            forest.TreeId,
            "DeleteSubtreeAsync(Documents): branch removed",
            details,
            cancellationToken);

        await FolderDisplay.RequireNamesAsync(
            context,
            forest.TreeId,
            ["System", "Shared", "Public"],
            "Subtree deletion did not remove Documents and both descendants.",
            cancellationToken);

        await folders.DeleteTreeAsync(forest.ArchiveTreeId, cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            forest.ArchiveTreeId,
            "DeleteTreeAsync(Archive): entire tree removed",
            details,
            cancellationToken);

        SampleConsole.Require(
            !await folders
                .InTree(forest.ArchiveTreeId)
                .Nodes
                .AnyAsync(cancellationToken),
            "DeleteTreeAsync left an archive folder visible.");

        SampleConsole.Require(
            await folders
                .InTree(forest.TreeId)
                .Nodes
                .CountAsync(cancellationToken)
            == 3,
            "Whole-tree deletion changed the unrelated main tree.");

        await SampleConsole.WriteLineAsync(
            "The archive TreeId remains reserved by a tombstone; ordinary deletion does not make it reusable.",
            cancellationToken);
    }
}
