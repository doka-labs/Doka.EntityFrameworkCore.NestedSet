namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Receives database identities while retaining explicit input sibling order.</summary>
public sealed class BulkManualNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the immediate nullable parent foreign key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the isolated forest scope.</summary>
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
