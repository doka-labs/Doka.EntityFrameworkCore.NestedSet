namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Identifies PostgreSQL cases.</summary>
public readonly struct PostgreSqlEngine : IProviderEngine
{
    /// <inheritdoc />
    public static string Name => "PostgreSql";
}
