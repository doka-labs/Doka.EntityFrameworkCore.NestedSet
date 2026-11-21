namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>The physical, ordered definition of a nonprimary index.</summary>
/// <param name="Name">The physical database index name.</param>
/// <param name="Columns">The indexed column names in key order.</param>
/// <param name="Descending">Whether each corresponding key column is descending.</param>
/// <param name="IsUnique">Whether the index enforces uniqueness.</param>
/// <param name="IsOrdinary">Whether every key is full length, ascending, valid and unfiltered on a B-tree.</param>
public sealed record MigrationIndex(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<bool> Descending,
    bool IsUnique,
    bool IsOrdinary
);
