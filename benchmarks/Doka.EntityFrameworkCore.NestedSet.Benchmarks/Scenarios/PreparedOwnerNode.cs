namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Provides a synthetic hierarchy whose scope is an ordinary tracked principal's key.</summary>
internal sealed class PreparedOwnerNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the direct parent key within one tree.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets synthetic application payload with no ordering semantics.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the independently maintained tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the persisted external owner key used as the hierarchy scope.</summary>
    public int Scope { get; set; } = 1;

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
