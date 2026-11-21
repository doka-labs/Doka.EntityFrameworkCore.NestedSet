namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Represents application data composed atomically with hierarchy operations.</summary>
public sealed class NativeCustomMarker
{
    /// <summary>Gets or sets the application-assigned identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets application data whose tracked state remains owned by the caller.</summary>
    public string Value { get; set; } = "";
}
