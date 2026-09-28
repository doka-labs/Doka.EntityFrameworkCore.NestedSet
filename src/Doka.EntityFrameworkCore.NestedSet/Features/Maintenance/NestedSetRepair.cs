namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Contains only the derived coordinates that must be repaired for one existing row.</summary>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
/// <param name="Key">The unchanged, canonical primary key identifying the row.</param>
/// <param name="Left">The reconstructed inclusive left boundary.</param>
/// <param name="Right">The reconstructed inclusive right boundary.</param>
/// <param name="Depth">The reconstructed number of ancestors.</param>
/// <param name="Position">The reconstructed zero-based sibling position.</param>
internal readonly record struct NestedSetRepair<TKey>(
    TKey Key,
    long Left,
    long Right,
    int Depth,
    long Position
)
    where TKey : notnull;
