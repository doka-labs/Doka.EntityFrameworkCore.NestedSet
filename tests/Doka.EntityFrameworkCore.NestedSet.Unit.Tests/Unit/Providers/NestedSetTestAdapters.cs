using Doka.EntityFrameworkCore.NestedSet.Providers;
using MySqlAdapter = Doka.EntityFrameworkCore.NestedSet.MySql.MySqlNestedSetProvider;
using PostgresAdapter = Doka.EntityFrameworkCore.NestedSet.Postgres.PostgresNestedSetProvider;
using SqliteAdapter = Doka.EntityFrameworkCore.NestedSet.Sqlite.SqliteNestedSetProvider;
using SqlServerAdapter = Doka.EntityFrameworkCore.NestedSet.SqlServer.SqlServerNestedSetProvider;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Selects actual adapter singletons for metadata and SQL contract tests without database access.</summary>
internal static class NestedSetTestAdapters
{
    /// <summary>Gets the concrete adapter whose behavior is asserted by an explicitly named test engine.</summary>
    internal static NestedSetProviderCapabilities ForEngine(string engine) => engine switch
    {
        "MySql" or "MariaDb" => MySqlAdapter.Instance,
        "PostgreSql" => PostgresAdapter.Instance,
        "Sqlite" => SqliteAdapter.Instance,
        "SqlServer" => SqlServerAdapter.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "The test engine is not registered."),
    };

    /// <summary>Builds an owned graph from the actual configured provider and adapter options.</summary>
    internal static ServiceProvider BuildServices(DbContextOptions options, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        // WHY: Deliberate metadata variants belong to the test's disposable service graph, not EF's global cache.
        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}
