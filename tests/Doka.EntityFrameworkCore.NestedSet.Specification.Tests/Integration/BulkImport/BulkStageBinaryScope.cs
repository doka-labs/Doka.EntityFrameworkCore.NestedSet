namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides mapped scalar inputs for exact BulkStageBinaryScope callback-guard regression tests.</summary>
public sealed class BulkStageBinaryScope : IScopedNestedSetNode<int, Guid, byte[]>
{
    /// <summary>Gets or sets the nullable direct parent identity.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the configured forest scope.</summary>
    public byte[] Scope { get; set; } = [];

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
