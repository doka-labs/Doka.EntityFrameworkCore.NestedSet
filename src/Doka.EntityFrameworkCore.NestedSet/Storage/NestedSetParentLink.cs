namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Associates a child with the canonical parent identity resolved using database comparison.</summary>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <param name="Key">The child identity.</param>
/// <param name="Parent">The matching parent's stored identity.</param>
internal readonly record struct NestedSetParentLink<TKey>(
    TKey Key,
    TKey Parent
)
    where TKey : notnull;
