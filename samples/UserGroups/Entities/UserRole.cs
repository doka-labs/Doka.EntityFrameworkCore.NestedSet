namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Grants a tenant-wide role to one user without replacing group membership.</summary>
public sealed class UserRole
{
    /// <summary>Gets or sets the authorized tenant identity.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the user identity.</summary>
    public int UserId { get; set; }

    /// <summary>Gets or sets the granted role.</summary>
    public Guid RoleId { get; set; }

    /// <summary>Gets or sets whether this grant is visible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Gets or sets the inclusive first valid day.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Gets or sets the inclusive last valid day.</summary>
    public DateOnly? ToDate { get; set; }
}
