namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Shows detached adjacency input imported as an atomic forest and as another branch.</summary>
internal static class BulkScenario
{
    /// <summary>Imports two independent trees, then adds a branch beneath an existing generated parent.</summary>
    /// <param name="context">The scenario's unit of work.</param>
    /// <param name="details">Whether output includes structural details.</param>
    /// <param name="cancellationToken">The token for all asynchronous work.</param>
    internal static async Task RunAsync(
        FileSystemContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("Bulk: import a forest and then another subtree", cancellationToken);

        var folders = context.NestedSet<Folder>();
        var treeId = Guid.Parse("55555555-5555-5555-5555-555555555501");
        var archiveTreeId = Guid.Parse("55555555-5555-5555-5555-555555555502");
        var root = new Folder("Imported");
        var projects = new Folder("Projects");
        var archive = new Folder("ImportedArchive");

        // WHY: Adjacency input works before identity keys exist; the import assigns parent keys and coordinates.
        await folders.InsertForestAsync(
            [
                new NestedSetTreeImport<Folder, Guid>(
                    treeId,
                    new NestedSetBranch<Folder>(
                        root,
                        [
                            new NestedSetBranch<Folder>(projects), new NestedSetBranch<Folder>(new Folder("Documents")),
                        ])),
                new NestedSetTreeImport<Folder, Guid>(
                    archiveTreeId,
                    new NestedSetBranch<Folder>(archive, [new NestedSetBranch<Folder>(new Folder("Snapshot")),])),
            ],
            cancellationToken);

        await FolderDisplay.TreeAsync(context, treeId, "Imported forest: main tree", details, cancellationToken);
        await FolderDisplay.TreeAsync(context, archiveTreeId, "Imported forest: archive", details, cancellationToken);

        var report = new Folder("Report");
        var attachments = new Folder("Attachments");

        await folders.InsertSubtreeAsync(
            new NestedSetBranch<Folder>(report, [new NestedSetBranch<Folder>(attachments),]),
            projects.Id,
            cancellationToken);

        await FolderDisplay.TreeAsync(
            context,
            treeId,
            "After importing Report with Attachments",
            details,
            cancellationToken);

        await FolderDisplay.RequireNamesAsync(
            context,
            treeId,
            [
                "Imported",
                "Documents",
                "Projects",
                "Report",
                "Attachments",
            ],
            "The bulk import did not preserve hierarchy preorder and configured sibling ordering.",
            cancellationToken);

        SampleConsole.Require(root.Id != 0, "Forest import did not return the generated root identity.");
        SampleConsole.Require(projects.ParentId == root.Id, "Forest import did not return the generated parent link.");
        SampleConsole.Require(report.ParentId == projects.Id, "Subtree import did not attach to the generated parent.");
        SampleConsole.Require(attachments.ParentId == report.Id, "Subtree import did not return the child parent link.");
        SampleConsole.Require(report.Depth == 2 && attachments.Depth == 3, "Imported depths are incorrect.");
    }
}
