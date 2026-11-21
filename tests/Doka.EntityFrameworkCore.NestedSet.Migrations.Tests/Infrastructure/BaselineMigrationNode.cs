namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>The pre-upgrade persisted hierarchy shape retained for migration compatibility tests.</summary>
public sealed class BaselineMigrationNode
{
    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public int NodeId { get; set; }

    /// <summary>Gets or sets the historical forest identifier.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the stable tree identity retained through the upgrade.</summary>
    public int TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identifier.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the historical Int32 left interval boundary.</summary>
    public int Left { get; set; }

    /// <summary>Gets or sets the historical Int32 right interval boundary.</summary>
    public int Right { get; set; }

    /// <summary>Gets or sets the depth below the root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the historical Int32 sibling position.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets data unrelated to hierarchy structure.</summary>
    public string Payload { get; set; } = "";

    /// <summary>Gets or sets an application-owned secondary sibling-order value.</summary>
    public int Category { get; set; }
}
