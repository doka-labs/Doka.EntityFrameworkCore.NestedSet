namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a hierarchy whose typed tree registries serialize concurrent structural writers.</summary>
public sealed class ConcurrentNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the application partition containing the tree.</summary>
    public int Tree { get; set; }

    /// <summary>Gets or sets the direct parent, or null for the tree root.</summary>
    public int? ParentId { get; set; }

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

    int IScopedNestedSetNode<int, Guid, int>.Scope => Tree;
}
