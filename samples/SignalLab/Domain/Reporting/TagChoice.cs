namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Defines one allowed fixed example value for a scalar field.</summary>
public sealed class TagChoice
{
    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the owner classification.</summary>
    public Guid TypeId { get; init; }

    /// <summary>Gets the defined field code.</summary>
    public string FieldCode { get; init; } = string.Empty;

    /// <summary>Gets one permitted case-sensitive value.</summary>
    public string Value { get; init; } = string.Empty;
}
