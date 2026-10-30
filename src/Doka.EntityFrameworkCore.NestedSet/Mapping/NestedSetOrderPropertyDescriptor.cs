namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Pairs one immutable sibling-order property with its exact direction.</summary>
/// <param name="Property">The copied property identity and scalar facets.</param>
/// <param name="Descending">Whether the property sorts descending.</param>
/// <param name="NullSortOrder">The explicit null placement, or null for a required property.</param>
internal readonly record struct NestedSetOrderPropertyDescriptor(
    NestedSetPropertyDescriptor Property,
    bool Descending,
    NullSortOrder? NullSortOrder
);
