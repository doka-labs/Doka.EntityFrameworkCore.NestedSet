namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Associates one user with one group in the same tenant.</summary>
public sealed class UserGroupMembership
{
    /// <summary>Gets or sets the tenant shared by both relationship endpoints.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the member's user key.</summary>
    public int UserId { get; set; }

    /// <summary>Gets or sets the assigned group key.</summary>
    public int UserGroupId { get; set; }
}
