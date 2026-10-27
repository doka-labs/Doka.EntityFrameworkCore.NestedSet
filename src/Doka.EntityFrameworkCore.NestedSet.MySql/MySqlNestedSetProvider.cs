using Doka.EntityFrameworkCore.MySql;

namespace Doka.EntityFrameworkCore.NestedSet.MySql;

/// <summary>Owns the immutable native MySql integration selected by the provider callback.</summary>
internal sealed partial class MySqlNestedSetProvider : NestedSetProviderCapabilities
{
    /// <summary>The stateless adapter shared only through its owning module and selected context options.</summary>
    internal static readonly MySqlNestedSetProvider Instance = new();

    /// <inheritdoc />
    internal override string ProviderName => "Doka.EntityFrameworkCore.MySql";

    /// <inheritdoc />
    internal override Assembly ProviderAssembly => typeof(MySqlDbContextOptionsBuilder).Assembly;

    /// <inheritdoc />
    internal override string TelemetryName => "mysql_mariadb";
}
