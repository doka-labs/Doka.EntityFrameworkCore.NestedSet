namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Shares runtime structural comparers for guards that know only EF property metadata.</summary>
internal static class NestedSetProviderComparer
{
    private static readonly ConditionalWeakTable<IProperty, NestedSetProviderComparer<object>> s_comparers = new();

    /// <summary>Checks whether a staged identity still has its exact provider representation.</summary>
    /// <param name="property">The finalized mapped identity property.</param>
    /// <param name="current">The value left by application callbacks.</param>
    /// <param name="staged">The value staged before the managed save.</param>
    /// <returns>Whether both values have the same provider representation.</returns>
    internal static bool Matches(
        IProperty property,
        object? current,
        object? staged
    ) => s_comparers
        .GetValue(property, static metadata => new NestedSetProviderComparer<object>(metadata))
        .Matches(current, staged);

    /// <summary>Hashes one non-null identity using the same provider comparison as <see cref="Matches" />.</summary>
    /// <param name="property">The finalized mapped identity property.</param>
    /// <param name="value">The non-null model value to hash.</param>
    /// <returns>A hash of the value's provider representation.</returns>
    internal static int GetHashCode(
        IProperty property,
        object value
    ) => s_comparers
        .GetValue(property, static metadata => new NestedSetProviderComparer<object>(metadata))
        .GetHashCode(value);
}
