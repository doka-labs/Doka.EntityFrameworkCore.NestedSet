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
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <returns>An isolated database owned by the caller and this engine fixture.</returns>
    public async Task<MigrationDatabase> CreateDatabaseAsync(
        string engine,
        CancellationToken cancellationToken,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        Action<IServiceCollection, DbContext>? configureDesignServices = null,
        bool qualifySchema = true
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            server = await StartServerAsync(engine, cancellationToken: cancellationToken);
            _servers.Add(engine, server);
        }

        var name = $"nestedset_{Guid.NewGuid():N}";
        await using var host = new MigrationDatabase(engine, server.Connection, null, null, null);
        await using var context = host.CreateContext();
        var sql = context.GetService<ISqlGenerationHelper>();
        var createDatabase = "CREATE DATABASE " + sql.DelimitIdentifier(name);
        await context.Database.ExecuteSqlRawAsync(createDatabase, cancellationToken);
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
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="engine">The selected database engine.</param>
    private static async Task<(string Connection, IContainer Container)> StartServerAsync(
        string engine,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        IContainer? owned = null;

        try
        {
            if (engine == "PostgreSql")
            {
                var postgres = new PostgreSqlBuilder(DatabaseTestTargets.PostgreSqlImage).Build();
                owned = postgres;
                await postgres.StartAsync(cancellationToken);

                return (postgres.GetConnectionString(), postgres);
            }

            if (engine == "SqlServer")
            {
                var sqlServer = new MsSqlBuilder(DatabaseTestTargets.SqlServerImage).Build();
                owned = sqlServer;
                await sqlServer.StartAsync(cancellationToken);

                // WHY: Savepoints require MARS to remain disabled for migration and hierarchy contracts.
                return (sqlServer.GetConnectionString() + ";MultipleActiveResultSets=False", sqlServer);
            }

            if (engine == "MySql")
            {
                // WHY: The fixture's isolated account provisions a database for each migration scenario.
                var mysql = new MySqlBuilder(DatabaseTestTargets.MySqlImage)
                    .WithUsername("root")
                    .Build();

                owned = mysql;
                await mysql.StartAsync(cancellationToken);

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

                owned = maria;
                await maria.StartAsync(cancellationToken);
                var connection = $"Server={maria.Hostname};Port={maria.GetMappedPublicPort(3306)};"
                    + "Database=nestedset;User ID=root;Password=nestedset-test-password;";

                return (connection, maria);
            }

            throw new ArgumentOutOfRangeException(nameof(engine));
        }
        catch (Exception failure) when (owned is not null)
        {
            try
            {
                // WHY: Canceled startup still owns its partially created container; finish disposal before retry.
                await owned.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Migration server startup and cleanup both failed.", failure, cleanupFailure);
            }

            throw;
        }
    }
}
