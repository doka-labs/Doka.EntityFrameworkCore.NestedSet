namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Generates at most 100,000 consecutive fixture IDs without allocating entity instances.</summary>
internal static class FixtureRowNumbers
{
    private const string Digits = "(SELECT 0 AS n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 "
        + "UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 "
        + "UNION ALL SELECT 8 UNION ALL SELECT 9)";

    /// <summary>Builds the shared nonrecursive number source for bounded native fixture inserts.</summary>
    /// <param name="offsetParameter">The provider-generated parameter name for the first ID.</param>
    /// <returns>A SELECT exposing the consecutive IDs as the id column.</returns>
    internal static string SelectSql(
        string offsetParameter
    )
    {
        // WHY: Digit joins work on every tested engine without recursion limits or a version-specific series API.

        return $"SELECT ({offsetParameter} + a.n + (10 * b.n) + (100 * c.n) + (1000 * d.n) + (10000 * e.n)) "
            + $"AS id FROM {Digits} a CROSS JOIN {Digits} b CROSS JOIN {Digits} c "
            + $"CROSS JOIN {Digits} d CROSS JOIN {Digits} e";
    }
}
