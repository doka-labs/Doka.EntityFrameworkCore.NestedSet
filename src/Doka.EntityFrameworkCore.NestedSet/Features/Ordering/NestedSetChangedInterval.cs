namespace Doka.EntityFrameworkCore.NestedSet.Features.Ordering;

/// <summary>Identifies inclusive persisted coordinates affected by a same-parent ordering operation.</summary>
internal readonly record struct NestedSetChangedInterval
{
    /// <summary>Creates a range; equal endpoints identify a root whose position alone changed.</summary>
    /// <param name="first">The first affected coordinate.</param>
    /// <param name="last">The last affected coordinate.</param>
    internal NestedSetChangedInterval(
        long first,
        long last
    )
    {
        First = first;
        Last = last;
    }

    /// <summary>Gets the first affected coordinate.</summary>
    internal long First { get; }

    /// <summary>Gets the last affected coordinate.</summary>
    internal long Last { get; }
}
