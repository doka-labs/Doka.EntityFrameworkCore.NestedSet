namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Shows validation, a read-only rebuild plan, and a rebuild of valid persisted adjacency.</summary>
internal static class MaintenanceScenario
{
    /// <summary>Inspects a healthy tree and proves that rebuilding it preserves its canonical structure.</summary>
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
            "Maintenance: validate, plan, and rebuild from stored adjacency",
            cancellationToken);

        var forest = await FolderForest.CreateAsync(
            context,
            Guid.Parse("66666666-6666-6666-6666-666666666601"),
            Guid.Parse("66666666-6666-6666-6666-666666666602"),
            cancellationToken);

        var tree = context
            .NestedSet<Folder>()
            .InTree(forest.TreeId);

        await FolderDisplay.TreeAsync(context, forest.TreeId, "Before inspection", details, cancellationToken);

        var quick = await tree.ValidateAsync(NestedSetValidationLevel.Quick, cancellationToken);
        var full = await tree.ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);
        var before = await tree.Nodes.ToListAsync(cancellationToken);
        var plan = await tree.PlanRebuildAsync(cancellationToken);

        await SampleConsole.WriteLineAsync(
            $"Quick valid={quick.IsValid}; Full valid={full.IsValid}; inspected folders={full.NodeCount}.",
            cancellationToken);

        await SampleConsole.WriteLineAsync(
            $"Plan: CanRebuild={plan.CanRebuild}, nodes={plan.NodeCount}, "
            + $"changed nodes={plan.ChangedNodeCount}, batches={plan.BatchCount}.",
            cancellationToken);

        SampleConsole.Require(quick.IsValid && full.IsValid, "A publicly created tree did not pass validation.");
        SampleConsole.Require(plan.CanRebuild, "Valid stored adjacency did not produce a rebuildable plan.");
        SampleConsole.Require(plan.NodeCount == 7, "The rebuild plan did not stay inside the selected tree.");
        SampleConsole.Require(plan.ChangedNodeCount == 0, "A healthy tree unexpectedly needs coordinate repair.");

        // WHY: The sample demonstrates the supported maintenance path without deliberately corrupting live columns.
        // Rebuild repairs derived coordinates only when the stored parent links and sibling positions are valid.
        await tree.RebuildAsync(cancellationToken);

        var after = await tree.Nodes.ToListAsync(cancellationToken);
        var validation = await tree.ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

        SampleConsole.Require(
            before
                .Select(folder => (folder.Id, folder.TreeId, folder.ParentId, folder.Left, folder.Right, folder.Depth,
                    folder.Position, folder.Name, folder.Category))
                .SequenceEqual(
                    after.Select(folder => (folder.Id, folder.TreeId, folder.ParentId, folder.Left, folder.Right,
                        folder.Depth, folder.Position, folder.Name, folder.Category))),
            "Rebuilding a canonical tree changed its structure or payload.");

        SampleConsole.Require(validation.IsValid, "The rebuilt tree did not pass full validation.");
        await FolderDisplay.TreeAsync(
            context,
            forest.TreeId,
            "After rebuild: same canonical tree",
            details,
            cancellationToken);
    }
}
