namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Represents a group in one tenant's hierarchy without requiring a tenant entity.</summary>
public sealed class UserGroup
{
    /// <summary>Gets or sets the generated group key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the tenant partition key; the hierarchy does not perform authorization.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the stable tree identity inside the tenant partition.</summary>
    public Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent key; null identifies the root.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the displayed group name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the maintained inclusive left boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the maintained inclusive right boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the maintained depth; roots have depth zero.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the maintained zero-based sibling position.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets the UTC payload timestamp maintained by the application's save customization.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
