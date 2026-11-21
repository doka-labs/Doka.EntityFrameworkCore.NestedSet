namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Receives its key and optional sort value from the database during the shared insertion save.</summary>
public sealed class BulkGeneratedNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the nullable immediate foreign key.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the provider-generated default or caller-assigned sort value.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the SQLite trigger-generated optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets inline complex payload whose generated leaves follow the mapped value contract.</summary>
    public BulkGeneratedDetails Details { get; set; } = new();

    /// <summary>Gets or sets SQLite JSON complex elements used to verify complete mapped-value rollback.</summary>
    public List<BulkGeneratedDetails> History { get; set; } = [];

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
