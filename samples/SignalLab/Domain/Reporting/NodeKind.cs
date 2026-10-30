namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Defines one synthetic instrument classification independently from its structural depth.</summary>
public sealed class NodeKind
{
    /// <summary>Gets the tenant owning this record.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Gets the classification identity.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the fixed case-sensitive example code.</summary>
    public string Code { get; init; } = string.Empty;
}
