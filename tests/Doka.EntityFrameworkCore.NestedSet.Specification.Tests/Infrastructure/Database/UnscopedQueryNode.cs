namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a scopeless hierarchy with an application-owned visibility filter.</summary>
public sealed class UnscopedQueryNode
{
    /// <summary>Gets or sets the application-assigned node identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity, or null for the root.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the zero-based sibling position.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets whether the application query filter exposes this node.</summary>
    public bool Visible { get; set; }
}
