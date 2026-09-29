namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Grants a permission through a role owned by the same tenant.</summary>
public sealed class RolePrivilege
{
    /// <summary>Gets or sets the tenant shared by both relationship endpoints.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the role providing the grant.</summary>
    public Guid RoleId { get; set; }

    /// <summary>Gets or sets the granted permission code.</summary>
    public string PrivilegeCode { get; set; } = string.Empty;
}
