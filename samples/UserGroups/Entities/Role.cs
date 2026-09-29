namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Defines a tenant-owned set of application privileges.</summary>
public sealed class Role
{
    /// <summary>Gets or sets the role key assigned by the application.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the tenant owning this role.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the displayed role name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC payload timestamp maintained by the application's save customization.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
