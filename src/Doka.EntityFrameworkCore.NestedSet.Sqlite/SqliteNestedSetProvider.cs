namespace Doka.EntityFrameworkCore.NestedSet.Sqlite;

/// <summary>Owns the immutable native Sqlite integration selected by the provider callback.</summary>
internal sealed partial class SqliteNestedSetProvider : NestedSetProviderCapabilities
{
    /// <summary>The stateless adapter shared only through its owning module and selected context options.</summary>
    internal static readonly SqliteNestedSetProvider Instance = new();

    /// <inheritdoc />
    internal override string ProviderName => "Microsoft.EntityFrameworkCore.Sqlite";

    /// <inheritdoc />
    internal override StringComparer IdentifierComparer => SqliteIdentifierComparer.Instance;

    /// <inheritdoc />

    // WHY: The SQLite provider does not support schemas; its DDL drops them, so equal names collide.
    // https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations
    internal override bool HonorsSchemas => false;

    /// <inheritdoc />
    internal override Assembly ProviderAssembly => typeof(SqliteDbContextOptionsBuilder).Assembly;

    /// <inheritdoc />
    internal override string TelemetryName => "sqlite";

    /// <inheritdoc />

    // WHY: SQLite reserves this prefix in any ASCII letter case and rejects CREATE TABLE for it.
    // https://www.sqlite.org/lang_createtable.html
    internal override bool IsReservedTableName(
        string name
    ) => SqliteIdentifierComparer.StartsWith(name, "sqlite_");
}
