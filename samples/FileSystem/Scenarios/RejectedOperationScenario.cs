namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Shows narrowly handled hierarchy precondition errors while verifying unchanged persisted data.</summary>
internal static class RejectedOperationScenario
{
    /// <summary>Attempts a cycle, a forbidden manual placement, and deletion of a root as a single node.</summary>
    /// <param name="context">The scenario's unit of work.</param>
    /// <param name="details">Whether output includes structural details.</param>
    /// <param name="cancellationToken">The token for all asynchronous work.</param>
    internal static async Task RunAsync(
        FileSystemContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync("Rejected operations: stable codes and unchanged trees", cancellationToken);

        var forest = await FolderForest.CreateAsync(
            context,
            Guid.Parse("77777777-7777-7777-7777-777777777701"),
            Guid.Parse("77777777-7777-7777-7777-777777777702"),
            cancellationToken);

        var folders = context.NestedSet<Folder>();
        var before = await folders
            .InTree(forest.TreeId)
            .Nodes
            .ToListAsync(cancellationToken);

        await FolderDisplay.TreeAsync(context, forest.TreeId, "Before rejected operations", details, cancellationToken);

        await RequireRejectionAsync(
            () => folders.MoveToAsync(forest.Projects.Id, forest.Repository.Id, cancellationToken),
            NestedSetErrorCode.CycleDetected,
            "Move Projects into its own descendant",
            cancellationToken);

        await RequireRejectionAsync(
            () => folders.MoveBeforeAsync(forest.Shared.Id, forest.Documents.Id, cancellationToken),
            NestedSetErrorCode.ManualPlacementNotAllowed,
            "Override strict alphabetic ordering with MoveBeforeAsync",
            cancellationToken);

        await RequireRejectionAsync(
            () => folders.DeleteAsync(forest.Root.Id, cancellationToken),
            NestedSetErrorCode.OperationRejected,
            "Delete a root as a single node",
            cancellationToken);

        var after = await folders
            .InTree(forest.TreeId)
            .Nodes
            .ToListAsync(cancellationToken);

        var validation = await folders
            .InTree(forest.TreeId)
            .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

        SampleConsole.Require(
            before
                .Select(folder => (folder.Id, folder.TreeId, folder.ParentId, folder.Left, folder.Right, folder.Depth,
                    folder.Position, folder.Name, folder.Category))
                .SequenceEqual(
                    after.Select(folder => (folder.Id, folder.TreeId, folder.ParentId, folder.Left, folder.Right,
                        folder.Depth, folder.Position, folder.Name, folder.Category))),
            "A rejected hierarchy operation changed persisted data.");

        SampleConsole.Require(validation.IsValid, "A rejected operation left an invalid tree.");
        await FolderDisplay.TreeAsync(context, forest.TreeId, "After rejection: unchanged", details, cancellationToken);
    }

    /// <summary>Handles the expected code and lets unexpected errors terminate the sample.</summary>
    /// <param name="operation">The public operation whose precondition is intentionally violated.</param>
    /// <param name="expectedCode">The stable code documenting the rejected operation.</param>
    /// <param name="description">The operation description displayed to the reader.</param>
    /// <param name="cancellationToken">The token for output.</param>
    private static async Task RequireRejectionAsync(
        Func<Task> operation,
        NestedSetErrorCode expectedCode,
        string description,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await operation();
        }
        catch (NestedSetException exception) when (exception.Code == expectedCode)
        {
            // WHY: Codes are the stable contract; matching message text would couple the sample to wording changes.
            await SampleConsole.WriteLineAsync($"{description}: rejected with {exception.Code}.", cancellationToken);

            return;
        }

        throw new InvalidOperationException($"{description} should have been rejected with {expectedCode}.");
    }
}
