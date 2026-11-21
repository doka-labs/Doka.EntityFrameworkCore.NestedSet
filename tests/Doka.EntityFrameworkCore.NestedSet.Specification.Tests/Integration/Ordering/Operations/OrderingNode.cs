namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Stores sorted domain values separately from maintained hierarchy coordinates.</summary>
public sealed class OrderingNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the optional direct parent.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the nullable ascending domain sort value; null values sort last.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the descending secondary sort value.</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets ordinary data that does not participate in ordering.</summary>
    public string Payload { get; set; } = "original";

    /// <summary>Gets or sets the converted domain order used by the converted model.</summary>
    public OrderingCategory Category { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the independent forest.</summary>
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
