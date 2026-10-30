namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Grants a role to one exact tenant-owned group membership.</summary>
public sealed class MembershipRole
{
    /// <summary>Gets or sets the authorized tenant identity.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the member identity.</summary>
    public int UserId { get; set; }

    /// <summary>Gets or sets the group identity.</summary>
    public int UserGroupId { get; set; }

    /// <summary>Gets or sets the locally granted role.</summary>
    public Guid RoleId { get; set; }

    /// <summary>Gets or sets whether this grant is authorized for the current sample view.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Gets or sets the inclusive first valid day, if constrained.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Gets or sets the inclusive last valid day, if constrained.</summary>
    public DateOnly? ToDate { get; set; }
}
