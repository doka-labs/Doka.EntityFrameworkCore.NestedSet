namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Assigns one scalar value to a node of the declared classification.</summary>
public sealed class TagValue
{
    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the owner node identity.</summary>
    public Guid NodeId { get; init; }

    /// <summary>Gets the owner classification checked by a relational foreign key.</summary>
    public Guid TypeId { get; init; }

    /// <summary>Gets the defined field code.</summary>
    public string FieldCode { get; init; } = string.Empty;

    /// <summary>Gets the fixed example value.</summary>
    public string Value { get; init; } = string.Empty;
}
