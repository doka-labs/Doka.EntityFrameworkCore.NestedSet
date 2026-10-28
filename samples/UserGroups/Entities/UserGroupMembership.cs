namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Associates one user with one group in the same tenant.</summary>
public sealed class UserGroupMembership
{
    /// <summary>Gets or sets the business leadership priority; a smaller value wins.</summary>
    public int LeadershipPriority { get; set; }

    /// <summary>Gets or sets whether this exact membership is visible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Gets or sets the inclusive first valid day, if constrained.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Gets or sets the inclusive last valid day, if constrained.</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>Gets or sets the tenant shared by both relationship endpoints.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the member's user key.</summary>
    public int UserId { get; set; }

    /// <summary>Gets or sets the assigned group key.</summary>
    public int UserGroupId { get; set; }
}
