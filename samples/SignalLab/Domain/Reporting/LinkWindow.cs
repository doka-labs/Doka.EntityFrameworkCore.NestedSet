namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Allows one exact direct parent-child edge for inclusive calendar days.</summary>
public sealed class LinkWindow
{
    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the direct parent identity.</summary>
    public Guid ParentId { get; init; }

    /// <summary>Gets the direct child identity.</summary>
    public Guid ChildId { get; init; }

    /// <summary>Gets the first allowed measurement day, inclusive.</summary>
    public DateOnly FromDate { get; init; }

    /// <summary>Gets the last allowed measurement day, inclusive, or an open end.</summary>
    public DateOnly? ToDate { get; init; }
}
