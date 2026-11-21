namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides mapped scalar inputs for exact BulkStageBinaryParent callback-guard regression tests.</summary>
public sealed class BulkStageBinaryParent : IScopedNestedSetNode<byte[], Guid, int>
{
    /// <summary>Gets or sets the nullable direct parent identity.</summary>
    public byte[]? ParentId { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public byte[] Id { get; set; } = [];

    /// <summary>Gets or sets the configured forest scope.</summary>
    public int Scope { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
