namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps two database-distinct scope values that represent the same instant.</summary>
internal sealed class OffsetScopeNode
{
    /// <summary>Gets or sets the forest scope including its UTC offset.</summary>
    internal DateTimeOffset Scope { get; set; }

    /// <summary>Gets or sets the node key within its scope.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the parent key.</summary>
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
}
