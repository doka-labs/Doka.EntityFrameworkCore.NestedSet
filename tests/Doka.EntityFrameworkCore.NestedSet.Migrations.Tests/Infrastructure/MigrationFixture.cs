namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Reuses real database engines with an independent database for every migration scenario.</summary>
/// <remarks>Consuming test classes serialize their cases; separate fixtures never share containers.</remarks>
public sealed class MigrationFixture : IAsyncLifetime
{
    private readonly Dictionary<string, (string Connection, IContainer Container)> _servers = new(
        StringComparer.Ordinal);

    /// <summary>Initializes the fixture; filtered cases start only the engines they need.</summary>
    /// <returns>An already completed task.</returns>
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Releases all server containers, including the isolated databases created inside them.</summary>
    /// <returns>A task that completes when container cleanup finishes.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var (_, container) in _servers.Values)
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>Creates an empty database without creating tables or invoking EnsureCreated.</summary>
    /// <param name="engine">Sqlite, MySql, MariaDb, PostgreSQL, or SqlServer.</param>
    /// <param name="configureOptions">Optional runtime integration registered after the ordinary provider.</param>
    /// <param name="configureDesignServices">Optional services registered after provider design services.</param>
    /// <param name="qualifySchema">Whether server tables use an explicit schema or the connection default.</param>
    /// <returns>An isolated database owned by the caller and this engine fixture.</returns>
    public async Task<MigrationDatabase> CreateDatabaseAsync(
        string engine,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        Action<IServiceCollection, DbContext>? configureDesignServices = null,
        bool qualifySchema = true
    )
    {
        if (engine == "Sqlite")
        {
            var file = Path.Combine(Path.GetTempPath(), $"nestedset-migrations-{Guid.NewGuid():N}.db");

            return new MigrationDatabase(
                engine,
                $"Data Source={file};Pooling=False",
                null,
                configureOptions,
                configureDesignServices,
                file);
        }

        SqlServerTestPlatform.RequireSupportedContainerHost(engine);

        if (!_servers.TryGetValue(engine, out var server))
        {
            server = await StartServerAsync(engine);
            _servers.Add(engine, server);
        }

        var name = $"nestedset_{Guid.NewGuid():N}";
        await using var host = new MigrationDatabase(engine, server.Connection, null, null, null);
        await using var context = host.CreateContext();
        var sql = context.GetService<ISqlGenerationHelper>();
        var createDatabase = "CREATE DATABASE " + sql.DelimitIdentifier(name);
        await context.Database.ExecuteSqlRawAsync(createDatabase, CancellationToken.None);
        var connection = new DbConnectionStringBuilder
        {
            ConnectionString = server.Connection,
            ["Database"] = name,
        };

        var schema = qualifySchema ? engine is "PostgreSql" or "SqlServer" ? "custom_hierarchy_schema" : name : null;

        return new MigrationDatabase(
            engine,
            connection.ConnectionString,
            schema,
            configureOptions,
            configureDesignServices);
    }

    /// <summary>Starts an owned server container, including the lifetime of its isolated databases.</summary>
    private static async Task<(string Connection, IContainer Container)> StartServerAsync(
        string engine
    )
    {
        if (engine == "PostgreSql")
        {
            var postgres = new PostgreSqlBuilder(DatabaseTestTargets.PostgreSqlImage).Build();
            await postgres.StartAsync(CancellationToken.None);

            return (postgres.GetConnectionString(), postgres);
        }

        if (engine == "SqlServer")
        {
            var sqlServer = new MsSqlBuilder(DatabaseTestTargets.SqlServerImage).Build();
            await sqlServer.StartAsync(CancellationToken.None);

            // WHY: Savepoints are part of the migration and hierarchy contracts and require MARS to remain disabled.
            return (sqlServer.GetConnectionString() + ";MultipleActiveResultSets=False", sqlServer);
        }

        if (engine == "MySql")
        {
            // WHY: The fixture provisions separate databases; its isolated container account needs to CREATE DATABASE.
            var mysql = new MySqlBuilder(DatabaseTestTargets.MySqlImage)
                .WithUsername("root")
                .Build();

            await mysql.StartAsync(CancellationToken.None);

            return (mysql.GetConnectionString(), mysql);
        }

        if (engine == "MariaDb")
        {
            var maria = new ContainerBuilder(DatabaseTestTargets.MariaDbImage)
                .WithEnvironment("MARIADB_ROOT_PASSWORD", "nestedset-test-password")
                .WithEnvironment("MARIADB_DATABASE", "nestedset")
                .WithPortBinding(3306, true)
                .WithWaitStrategy(
                    Wait
                        .ForUnixContainer()
                        .UntilCommandIsCompleted(
                            "mariadb",
                            "--protocol=tcp",
                            "-h127.0.0.1",
                            "-uroot",
                            "-pnestedset-test-password",
                            "-e",
                            "SELECT 1"))
                .Build();

            await maria.StartAsync(CancellationToken.None);
            var connection = $"Server={maria.Hostname};Port={maria.GetMappedPublicPort(3306)};"
                + "Database=nestedset;User ID=root;Password=nestedset-test-password;";

            return (connection, maria);
        }

        throw new ArgumentOutOfRangeException(nameof(engine));
    }
}
