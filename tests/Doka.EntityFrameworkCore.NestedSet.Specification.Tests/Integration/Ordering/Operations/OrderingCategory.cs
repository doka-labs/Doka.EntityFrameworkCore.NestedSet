namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides an order whose enum ordinals intentionally disagree with the converted text order.</summary>
public enum OrderingCategory
{
    /// <summary>The alphabetically last category with the smallest ordinal.</summary>
    Zeta,

    /// <summary>The alphabetically first category.</summary>
    Alpha,

    /// <summary>The middle category.</summary>
    Beta,
}
