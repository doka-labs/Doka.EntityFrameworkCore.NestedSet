namespace Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

/// <summary>Records an inclusive validity period for one stored direct group edge.</summary>
public sealed class GroupRelationPeriod
{
    /// <summary>Gets or sets the authorized tenant identity.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Gets or sets the direct parent identity.</summary>
    public int ParentId { get; set; }

    /// <summary>Gets or sets the direct child identity.</summary>
    public int ChildId { get; set; }

    /// <summary>Gets or sets the inclusive first valid day.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Gets or sets the inclusive last valid day; null remains open.</summary>
    public DateOnly? ToDate { get; set; }
}
