namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Stores an owned collection row whose identifier is unique only within its principal.</summary>
internal sealed class NativeIndexedTag
{
    /// <summary>Gets or sets the owner-local collection identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the owned collection payload.</summary>
    internal string Value { get; set; } = string.Empty;
}
