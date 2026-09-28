namespace Doka.EntityFrameworkCore.NestedSet.Shared.Structural;

/// <summary>Protects structural arithmetic from invalid persisted inputs.</summary>
internal static class NestedSetGuards
{
    /// <summary>Rejects locally invalid coordinates before they become operands of structural updates.</summary>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <param name="node">The snapshot whose bounds, depth, and position must be valid.</param>
    /// <exception cref="InvalidOperationException">The snapshot contains an invalid coordinate.</exception>
    /// <remarks>This checks one node and does not replace validation of the entire tree.</remarks>
    internal static void RequireBounds<TKey>(
        NestedSetNode<TKey> node
    )
        where TKey : notnull
    {
        try
        {
            // WHY: Core owns the interval invariant; EF adds persisted depth and sibling-position preconditions.
            _ = new NestedSetBounds(node.Left, node.Right);
        }
        catch (ArgumentException error)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidStructure,
                "Invalid nested-set coordinates. Validate and rebuild the tree first.",
                error);
        }

        if (node.Depth < 0
            || node.Position < 0)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidStructure,
                "Invalid nested-set coordinates. Validate and rebuild the tree first.");
        }
    }
}
