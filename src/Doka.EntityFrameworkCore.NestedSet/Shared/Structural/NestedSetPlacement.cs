namespace Doka.EntityFrameworkCore.NestedSet.Shared.Structural;

/// <summary>Identifies a node's position relative to another node.</summary>
internal enum NestedSetPlacement
{
    /// <summary>Place as the first child.</summary>
    FirstChild,

    /// <summary>Place as the last child.</summary>
    LastChild,

    /// <summary>Place immediately before the sibling.</summary>
    Before,

    /// <summary>Place immediately after the sibling.</summary>
    After,
}
