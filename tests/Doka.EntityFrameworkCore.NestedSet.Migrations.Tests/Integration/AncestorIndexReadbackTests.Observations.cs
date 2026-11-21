namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexReadbackTests
{
    /// <summary>Reads generated shadow values independently of the detached input graph.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static Task<ReadbackRow[]> ReadRowsAsync(
        AncestorIndexReadbackContext context,
        CancellationToken cancellationToken
    ) => context
        .Set<AncestorIndexReadbackNode>()
        .AsNoTracking()
        .OrderBy(node => node.Id)
        .Select(node => new ReadbackRow(
            node.Id,
            node.Scope,
            node.TreeId,
            node.ParentId,
            node.Left,
            node.Right,
            node.Depth,
            node.Position,
            node.Input,
            node.Stamp,
            EF.Property<bool>(node, AncestorIndexReadbackContext.FlagProperty)))
        .ToArrayAsync(cancellationToken);

    /// <summary>Captures caller-visible scalar values without attaching the input entity.</summary>
    private static ReadbackRow Capture(
        AncestorIndexReadbackNode node,
        bool isBranch
    ) => new(
        node.Id,
        node.Scope,
        node.TreeId,
        node.ParentId,
        node.Left,
        node.Right,
        node.Depth,
        node.Position,
        node.Input,
        node.Stamp,
        isBranch);
}
