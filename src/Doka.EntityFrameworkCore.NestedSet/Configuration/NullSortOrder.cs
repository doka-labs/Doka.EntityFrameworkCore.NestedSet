namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Controls where null values appear within one sibling-order criterion.</summary>
public enum NullSortOrder
{
    /// <summary>Places null values before non-null values.</summary>
    First = 0,

    /// <summary>Places null values after non-null values.</summary>
    Last = 1,
}
