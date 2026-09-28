namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Names structural roles independently of configured CLR properties and database columns.</summary>
internal static class NestedSetPropertyRoles
{
    // WHY: Role names form persisted annotation keys. Explicit values keep source renames from changing metadata.

    /// <summary>Contains every structural role recognized in model annotations.</summary>
    internal static readonly string[] All =
    [
        NodeKey,
        Scope,
        TreeId,
        Parent,
        Left,
        Right,
        Depth,
        Position,
    ];

    /// <summary>Identifies the stable scalar key used by hierarchy operations.</summary>
    internal const string NodeKey = "NodeKey";

    /// <summary>Identifies the stable tree containing the node.</summary>
    internal const string TreeId = "TreeId";

    /// <summary>Identifies the scope that isolates independent forests.</summary>
    internal const string Scope = "Scope";

    /// <summary>Identifies the nullable parent key.</summary>
    internal const string Parent = "Parent";

    /// <summary>Identifies the inclusive left boundary.</summary>
    internal const string Left = "Left";

    /// <summary>Identifies the inclusive right boundary.</summary>
    internal const string Right = "Right";

    /// <summary>Identifies the persisted number of ancestors.</summary>
    internal const string Depth = "Depth";

    /// <summary>Identifies the zero-based position among siblings.</summary>
    internal const string Position = "Position";
}
