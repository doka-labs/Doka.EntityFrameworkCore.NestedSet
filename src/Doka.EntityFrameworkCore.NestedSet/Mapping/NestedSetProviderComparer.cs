namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Compares model values by their provider representations during structural work.</summary>
/// <typeparam name="TValue">The mapped model value type.</typeparam>
internal sealed class NestedSetProviderComparer<TValue> : IEqualityComparer<TValue>
    where TValue : notnull
{
    private readonly ValueConverter? _converter;
    private readonly ValueComparer _comparer;
    private readonly ValueComparer<TValue>? _typedComparer;

    /// <summary>Uses the finalized conversion instead of potentially broader model equality.</summary>
    /// <param name="property">The mapped property, or null for a missing optional role.</param>
    internal NestedSetProviderComparer(
        IProperty? property
    )
    {
        _converter = property?.GetTypeMapping().Converter;
        var providerType = _converter?.ProviderClrType ?? property?.ClrType ?? typeof(TValue);

        // WHY: SQL may distinguish converted values whose model type implements broader equality.
        // Native values retain a typed fast path; binary provider values need structural equality.
        _comparer = ValueComparer.CreateDefault(providerType, favorStructuralComparisons: true);
        _typedComparer = _converter is null ? _comparer as ValueComparer<TValue> : null;

        if (_converter is null
            && Nullable.GetUnderlyingType(providerType) == typeof(TValue))
        {
            // WHY: A present Parent<TKey> owns the underlying non-null key while its EF property is nullable.
            // Compare that native key directly; absent parents are handled by their separate presence flag.
            _typedComparer = (ValueComparer<TValue>)ValueComparer.CreateDefault<TValue>(
                favorStructuralComparisons: true);
        }
    }

    /// <inheritdoc />
    public bool Equals(
        TValue? first,
        TValue? second
    ) => // WHY: ReferenceEquals requires object arguments. Checking it first boxes native value types in
        // Tier0, multiplying allocations for every tracked candidate and requested tree comparison.
        _typedComparer?.Equals(first, second)
        // WHY: Matches already handles reference and null values. Calling it once avoids redundant input
        // boxing for converted value types before the required to be erased provider-converter boundary.
        ?? Matches(first, second);

    /// <inheritdoc />
    public int GetHashCode(
        TValue value
    )
    {
        if (_typedComparer is { } typed)
        {
            return typed.GetHashCode(value);
        }

        var providerValue = _converter is null ? value : _converter.ConvertToProvider(value);

        return _comparer.GetHashCode(providerValue!);
    }

    /// <summary>Checks one pair whose structural role is known only at runtime.</summary>
    /// <param name="first">The current model value.</param>
    /// <param name="second">The staged model value.</param>
    /// <returns>Whether both values have the same provider representation.</returns>
    internal bool Matches(
        object? first,
        object? second
    )
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is null
            || second is null)
        {
            return false;
        }

        var firstValue = _converter is null ? first : _converter.ConvertToProvider(first);
        var secondValue = _converter is null ? second : _converter.ConvertToProvider(second);

        return _comparer.Equals(firstValue, secondValue);
    }
}

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
