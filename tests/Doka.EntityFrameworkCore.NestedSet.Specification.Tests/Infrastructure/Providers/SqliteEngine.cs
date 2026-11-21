namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies SQLite cases.</summary>
public readonly struct SqliteEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "Sqlite";
}
