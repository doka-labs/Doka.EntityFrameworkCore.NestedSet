namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Stores every structural identity and coordinate in the ancestor table of a TPT hierarchy.</summary>
internal abstract class TptInheritedCollationNode
{
    /// <summary>Gets or sets the assigned string node key.</summary>
    internal string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the native Scope owning the complete tree.</summary>
    internal string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable tree identity within the Scope.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the optional direct-parent string key.</summary>
    internal string? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application payload stored alongside the structural columns.</summary>
    internal string Name { get; set; } = string.Empty;
}
