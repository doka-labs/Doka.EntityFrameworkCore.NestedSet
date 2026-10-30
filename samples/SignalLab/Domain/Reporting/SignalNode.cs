namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Represents an acquisition instrument while all structural roles remain shadow properties.</summary>
public sealed class SignalNode
{
    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the node identity inside its tenant.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the station, array, module or sensor kind independently from depth.</summary>
    public Guid TypeId { get; init; }

    /// <summary>Gets or sets the displayed label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether ordinary readers may see this endpoint.</summary>
    public bool IsVisible { get; set; } = true;
}
