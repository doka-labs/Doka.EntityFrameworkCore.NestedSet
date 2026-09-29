namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Assigns a tenant-owned role directly to a group of the same tenant.</summary>
public sealed class UserGroupRole
{
    /// <summary>Gets or sets the tenant shared by both relationship endpoints.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the group receiving the role.</summary>
    public int UserGroupId { get; set; }

    /// <summary>Gets or sets the assigned role key.</summary>
    public Guid RoleId { get; set; }
}
