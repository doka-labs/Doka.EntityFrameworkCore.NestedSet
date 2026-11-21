namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>A current persisted hierarchy row using stable tree identity and Int64 coordinates.</summary>
public sealed class MigrationNode
{
    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public int NodeId { get; set; }

    /// <summary>Gets or sets the application scope that partitions tree identities.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public int TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identifier.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the Int64 left interval boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the Int64 right interval boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the depth below the root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the Int64 sibling position.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets data unrelated to the hierarchy's structural coordinates.</summary>
    public string Payload { get; set; } = "";

    /// <summary>Gets or sets an application-owned secondary sibling-order value.</summary>
    public int Category { get; set; }
}
