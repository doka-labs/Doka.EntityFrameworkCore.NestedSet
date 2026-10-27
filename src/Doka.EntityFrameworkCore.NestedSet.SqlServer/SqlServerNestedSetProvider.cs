namespace Doka.EntityFrameworkCore.NestedSet.SqlServer;

/// <summary>Owns the immutable native SqlServer integration selected by the provider callback.</summary>
internal sealed partial class SqlServerNestedSetProvider : NestedSetProviderCapabilities
{
    /// <summary>The stateless adapter shared only through its owning module and selected context options.</summary>
    internal static readonly SqlServerNestedSetProvider Instance = new();

    /// <inheritdoc />
    internal override string ProviderName => "Microsoft.EntityFrameworkCore.SqlServer";

    /// <inheritdoc />
    internal override Assembly ProviderAssembly => typeof(SqlServerDbContextOptionsBuilder).Assembly;

    /// <inheritdoc />
    internal override string TelemetryName => "sql_server";
}
