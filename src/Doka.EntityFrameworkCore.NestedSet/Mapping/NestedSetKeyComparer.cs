namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Preserves EF key semantics when a structural plan uses in-memory dictionaries.</summary>
/// <typeparam name="TKey">The non-null primary key type.</typeparam>
internal sealed class NestedSetKeyComparer<TKey> : IEqualityComparer<TKey>
    where TKey : notnull
{
    private readonly ValueComparer _comparer;

    private readonly ValueComparer<TKey>? _typedComparer;

    /// <summary>Adapts the finalized model's key comparer without changing binary content equality.</summary>
    /// <param name="comparer">The EF key comparer.</param>
    internal NestedSetKeyComparer(
        ValueComparer comparer
    )
    {
        _comparer = comparer;
        // WHY: Typed EF comparers avoid boxing dictionary keys; unusual custom comparers retain their exact fallback.
        _typedComparer = comparer as ValueComparer<TKey>;
    }

    /// <inheritdoc />
    public bool Equals(
        TKey? first,
        TKey? second
    ) => _typedComparer?.Equals(first!, second!) ?? _comparer.Equals(first, second);

    /// <inheritdoc />
    public int GetHashCode(
        TKey value
    ) => _typedComparer is not null
        ? _typedComparer.GetHashCode(value)
        : _comparer.GetHashCode(value);
}
