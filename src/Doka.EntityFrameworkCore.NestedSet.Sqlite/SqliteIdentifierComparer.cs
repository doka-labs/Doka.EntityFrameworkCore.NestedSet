namespace Doka.EntityFrameworkCore.NestedSet.Sqlite;

/// <summary>Compares identifiers the way SQLite does, folding only the ASCII letters A to Z.</summary>
/// <remarks>
///     SQLite compares names byte by byte through an ASCII-only case table, so U+00C4 and U+00E4 stay distinct.
///     UTF-8 encodes every non-ASCII character with bytes above 0x7F, which that table never folds, so folding the
///     same ASCII letters in UTF-16 yields the same equality.
///     https://www.sqlite.org/c3ref/stricmp.html
///     https://github.com/sqlite/sqlite/blob/master/src/global.c
/// </remarks>
internal sealed class SqliteIdentifierComparer : StringComparer
{
    /// <summary>The stateless comparer shared by the SQLite provider.</summary>
    internal static readonly SqliteIdentifierComparer Instance = new();

    private SqliteIdentifierComparer() { }

    /// <inheritdoc />
    public override int Compare(
        string? x,
        string? y
    )
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var length = Math.Min(x.Length, y.Length);

        for (var index = 0; index < length; index++)
        {
            var difference = Fold(x[index]) - Fold(y[index]);

            if (difference != 0)
            {
                return difference;
            }
        }

        return x.Length - y.Length;
    }

    /// <inheritdoc />
    public override bool Equals(
        string? x,
        string? y
    ) => Compare(x, y) == 0;

    /// <inheritdoc />
    public override int GetHashCode(
        string obj
    )
    {
        var hash = new HashCode();

        foreach (var character in obj)
        {
            hash.Add(Fold(character));
        }

        return hash.ToHashCode();
    }

    /// <summary>Tests a prefix under the same folding, as SQLite does for its reserved names.</summary>
    /// <param name="name">The candidate identifier.</param>
    /// <param name="prefix">The prefix to look for.</param>
    /// <returns>Whether the identifier starts with the prefix.</returns>
    internal static bool StartsWith(
        string name,
        string prefix
    ) => name.Length >= prefix.Length && Instance.Equals(name[..prefix.Length], prefix);

    /// <summary>Lowers an ASCII capital and leaves every other character unchanged.</summary>
    private static char Fold(
        char character
    ) => character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A')) : character;
}
