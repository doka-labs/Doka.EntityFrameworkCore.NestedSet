namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a hierarchy whose typed tree registries serialize concurrent structural writers.</summary>
public sealed class ConcurrentNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }

    /// <summary>Gets or sets the application partition containing the tree.</summary>
    public int Tree { get; set; }

    int IScopedNestedSetNode<int, Guid, int>.Scope => Tree;

    /// <summary>Gets or sets the direct parent, or null for the tree root.</summary>
    public int? ParentId { get; set; }
}

/// <summary>Represents application data composed atomically with hierarchy operations.</summary>
public sealed class UnrelatedRow
{
    /// <summary>Gets or sets the application-assigned identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets application data whose tracked state remains owned by the caller.</summary>
    public string Value { get; set; } = "";
}

/// <summary>Represents application audit data whose key the database generates during persistence.</summary>
public sealed class GeneratedAuditRow
{
    /// <summary>Gets or sets the store-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the audited operation description.</summary>
    public string Value { get; set; } = "";
}

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
