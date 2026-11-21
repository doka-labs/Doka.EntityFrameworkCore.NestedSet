namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Flattens a batched catalog read without materializing domain entities.</summary>
internal sealed class SignalCatalogRow
{
    /// <summary>Gets the row kind: anchor, channel, field definition, or allowed choice.</summary>
    public int Kind { get; init; }

    /// <summary>Gets an anchor or channel identity when relevant.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets a metadata owner or measurement classification.</summary>
    public Guid TypeId { get; init; }

    /// <summary>Gets a field code, or the empty string for another row kind.</summary>
    public string FieldCode { get; init; } = string.Empty;

    /// <summary>Gets a fixed allowed choice, or the empty string.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Gets a channel's additive unit.</summary>
    public string UnitCode { get; init; } = string.Empty;
}
