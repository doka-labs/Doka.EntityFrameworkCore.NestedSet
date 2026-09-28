namespace Doka.EntityFrameworkCore.NestedSet.Providers;

/// <summary>Identifies the explicitly supported relational dialects without depending on provider packages.</summary>
internal enum NestedSetProviderKind
{
    /// <summary>The Microsoft SQLite provider and its single-writer transaction protocol.</summary>
    Sqlite,

    /// <summary>The Doka provider shared by MySQL and MariaDB.</summary>
    MySql,

    /// <summary>The Npgsql PostgreSQL provider.</summary>
    PostgreSql,

    /// <summary>The Microsoft SQL Server provider.</summary>
    SqlServer,
}
