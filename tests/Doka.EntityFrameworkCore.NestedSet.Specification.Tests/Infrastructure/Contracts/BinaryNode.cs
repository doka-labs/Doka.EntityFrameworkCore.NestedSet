namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a hierarchy node whose array key must be compared by value.</summary>
public sealed class BinaryNode : IScopedNestedSetNode<byte[], Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the isolated hierarchy scope.</summary>
    public int Tree { get; set; }

    int IScopedNestedSetNode<byte[], Guid, int>.Scope => Tree;

    /// <summary>Gets or sets the parent key, or null for a root.</summary>
    public byte[]? ParentId { get; set; }

    /// <inheritdoc />
    public byte[] Id { get; set; } = [];

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
