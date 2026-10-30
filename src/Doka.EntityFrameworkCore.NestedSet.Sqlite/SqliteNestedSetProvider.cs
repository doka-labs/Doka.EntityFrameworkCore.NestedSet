namespace Doka.EntityFrameworkCore.NestedSet.Sqlite;

/// <summary>Owns the immutable native Sqlite integration selected by the provider callback.</summary>
internal sealed partial class SqliteNestedSetProvider : NestedSetProviderCapabilities
{
    /// <summary>The stateless adapter shared only through its owning module and selected context options.</summary>
    internal static readonly SqliteNestedSetProvider Instance = new();

    /// <inheritdoc />
    internal override string ProviderName => "Microsoft.EntityFrameworkCore.Sqlite";

    /// <inheritdoc />
    internal override Assembly ProviderAssembly => typeof(SqliteDbContextOptionsBuilder).Assembly;

    /// <inheritdoc />
    internal override string TelemetryName => "sqlite";
}
