namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Identifies a mapped hierarchy role without exposing model property names as string contracts.</summary>
[PublicAPI]
public enum NestedSetStructuralRole
{
    /// <summary>The inclusive left preorder boundary.</summary>
    Left = 0,

    /// <summary>The inclusive right preorder boundary.</summary>
    Right = 1,

    /// <summary>The number of ancestors above a node.</summary>
    Depth = 2,

    /// <summary>The zero-based position inside a sibling group.</summary>
    Position = 3,
}
