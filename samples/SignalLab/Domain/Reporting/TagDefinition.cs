namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Defines one allowed scalar field on one instrument classification.</summary>
public sealed class TagDefinition
{
    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the owner classification.</summary>
    public Guid TypeId { get; init; }

    /// <summary>Gets the case-sensitive field code.</summary>
    public string FieldCode { get; init; } = string.Empty;

    /// <summary>Gets the field display label.</summary>
    public string Label { get; init; } = string.Empty;
}
