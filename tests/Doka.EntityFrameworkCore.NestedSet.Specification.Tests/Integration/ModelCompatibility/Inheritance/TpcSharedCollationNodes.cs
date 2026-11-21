namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares inherited identity properties across two concrete tables with different Scope equality.</summary>
internal abstract class CollatedTpcNode
{
    /// <summary>Gets or sets the assigned key shared in metadata but stored in each concrete table.</summary>
    internal string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the independently stored namespace of each tree.</summary>
    internal string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the complete tree identity inside its Scope.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the optional direct parent under the concrete table's key comparison.</summary>
    internal string? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application payload preserved across failed hierarchy writes.</summary>
    internal string Name { get; set; } = string.Empty;
}
