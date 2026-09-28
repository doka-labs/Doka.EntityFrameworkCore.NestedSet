namespace Doka.EntityFrameworkCore.NestedSet.Features.Ordering;

/// <summary>Projects only the two structural values needed to place a subtree after its ordered predecessor.</summary>
internal sealed class NestedSetPredecessor
{
    /// <summary>Gets or sets the predecessor's inclusive right boundary, or null for the first sibling.</summary>
    public long? Right { get; set; }

    /// <summary>Gets or sets the predecessor's current sibling position, or null for the first sibling.</summary>
    public long? Position { get; set; }
}
