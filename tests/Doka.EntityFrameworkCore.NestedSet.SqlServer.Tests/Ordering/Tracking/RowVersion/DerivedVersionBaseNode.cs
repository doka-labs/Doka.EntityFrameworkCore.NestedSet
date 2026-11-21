namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Shares structural roles and payload across the configured base owner and its concrete subtypes.</summary>
public class DerivedVersionBaseNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <inheritdoc />
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the sibling ordering value.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets ordinary payload excluded from structural refresh projections.</summary>
    public string Payload { get; set; } = "Unchanged payload";

    /// <inheritdoc />
    public int Id { get; set; }

    /// <inheritdoc />
    public Guid TreeId { get; set; }

    /// <inheritdoc />
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
