namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Defines a summable channel and its authoritative measurement classification.</summary>
public sealed class SignalChannel
{
    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the channel identity.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the classification whose measurements may contribute.</summary>
    public Guid MeasurementTypeId { get; init; }

    /// <summary>Gets the channel display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the declared additive unit.</summary>
    public string UnitCode { get; init; } = string.Empty;
}
