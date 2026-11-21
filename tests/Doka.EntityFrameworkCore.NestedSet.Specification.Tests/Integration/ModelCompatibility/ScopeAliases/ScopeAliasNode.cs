namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a hierarchy with a native integer NodeKey and a case-insensitive text scope.</summary>
internal sealed class ScopeAliasNode
{
    /// <summary>Gets or sets the native integer hierarchy identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the tenant whose database comparison ignores letter case.</summary>
    internal string Scope { get; set; } = "";

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the configured sibling ordering value.</summary>
    internal string Name { get; set; } = "";

    /// <summary>Copies persisted values under another spelling of the same database scope.</summary>
    internal ScopeAliasNode WithScope(
        string scope
    ) => new()
    {
        Id = Id,
        Scope = scope,
        TreeId = TreeId,
        ParentId = ParentId,
        Left = Left,
        Right = Right,
        Depth = Depth,
        Position = Position,
        Name = Name,
    };
}
