namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Shows an ordinary tracked payload edit automatically moving the complete renamed subtree.</summary>
internal static class RenameScenario
{
    /// <summary>Renames Shared to Assets and verifies stable hierarchy preorder and sibling position.</summary>
    /// <param name="context">The scenario's unit of work.</param>
    /// <param name="details">Whether output includes structural details.</param>
    /// <param name="cancellationToken">The token for all asynchronous work.</param>
    internal static async Task RunAsync(
        FileSystemContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(
            "Rename: automatic sibling ordering during SaveChangesAsync",
            cancellationToken);

        var forest = await FolderForest.CreateAsync(
            context,
            Guid.Parse("22222222-2222-2222-2222-222222222201"),
            Guid.Parse("22222222-2222-2222-2222-222222222202"),
            cancellationToken);

        await FolderDisplay.TreeAsync(context, forest.TreeId, "Before rename", details, cancellationToken);

        // WHY: Normal DbSet reads track the entity. The context coordinates its sort-field change at save time.
        var shared = await context.Folders.SingleAsync(folder => folder.Id == forest.Shared.Id, cancellationToken);

        shared.Name = "Assets";
        await context.SaveChangesAsync(cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            forest.TreeId,
            "After renaming Shared to Assets",
            details,
            cancellationToken);

        await FolderDisplay.RequireNamesAsync(
            context,
            forest.TreeId,
            [
                "System",
                "Assets",
                "Public",
                "Documents",
                "Projects",
                "NestedSet",
                "Notes",
            ],
            "The renamed subtree was not moved before Documents.",
            cancellationToken);

        SampleConsole.Require(shared.Position == 0, "SaveChangesAsync did not refresh the renamed folder's position.");
        await SampleConsole.WriteLineAsync(
            "Assets is now position 0; Public moved together with its parent.",
            cancellationToken);
    }
}
