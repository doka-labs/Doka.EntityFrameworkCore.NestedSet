using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Postgres;

/// <summary>Owns the immutable native Postgres integration selected by the provider callback.</summary>
internal sealed partial class PostgresNestedSetProvider : NestedSetProviderCapabilities
{
    /// <summary>The stateless adapter shared only through its owning module and selected context options.</summary>
    internal static readonly PostgresNestedSetProvider Instance = new();

    /// <inheritdoc />
    internal override string ProviderName => "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <inheritdoc />
    internal override Assembly ProviderAssembly => typeof(NpgsqlDbContextOptionsBuilder).Assembly;

    /// <inheritdoc />
    internal override string TelemetryName => "postgresql";
}
