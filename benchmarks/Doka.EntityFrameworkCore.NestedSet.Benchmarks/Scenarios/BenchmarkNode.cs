namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Stores representative application payload and all independently maintained structural roles.</summary>
public sealed class BenchmarkNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the independent tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the application partition.</summary>
    public int Scope { get; set; } = 1;

    /// <summary>Gets or sets the direct parent key.</summary>
    public int? ParentId { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }

    /// <summary>Gets or sets the deterministic sibling sort value.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the one-kibibyte payload materialized by full-entity queries.</summary>
    public string Payload { get; set; } = string.Empty;
}
