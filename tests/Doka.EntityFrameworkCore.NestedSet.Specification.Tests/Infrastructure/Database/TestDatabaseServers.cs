using System.Collections.Concurrent;
using MySqlConnector;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares one container per server engine across an integration-test assembly.</summary>
/// <remarks>Each caller receives a distinct database, so xUnit collections can run concurrently.</remarks>
public sealed class TestDatabaseServers : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<Server>>> _servers = new(
        StringComparer.Ordinal);

    /// <summary>Resolves the server owner of the currently executing provider test assembly.</summary>
    /// <returns>The fixture registered by that executable assembly.</returns>
    /// <remarks>Call from a class fixture or test, where xUnit exposes fixture mappings.</remarks>
    internal static async ValueTask<TestDatabaseServers> GetCurrentAsync()
    {
        // WHY: The specification library can serve multiple test assemblies in one process; a static owner
        // would couple their container lifetimes and allow one assembly to dispose another's servers.
        var fixture = await TestContext.Current.GetFixture<TestDatabaseServers>();

        return fixture
            ?? throw new InvalidOperationException("The current test assembly has no database-server fixture.");
    }

    /// <summary>Creates a connection string for a new database on the shared server.</summary>
    /// <param name="engine">MySql, MariaDb, PostgreSQL, or SqlServer.</param>
    /// <returns>A connection string with a database name unique to this caller.</returns>
    internal async Task<string> NewConnectionStringAsync(
        string engine
    )
    {
        if (!ProviderEngineOwnership.Includes(engine))
        {
            throw new InvalidOperationException($"The current test assembly does not own the {engine} server.");
        }

        SqlServerTestPlatform.RequireSupportedContainerHost(engine);

        // WHY: Lazy ensures simultaneous xUnit collections cannot each start an expensive server container.
        var server = await _servers.GetOrAdd(engine, static name => new Lazy<Task<Server>>(() => StartAsync(name))).Value;

        var databaseName = "nestedset_"
            + Guid
                .NewGuid()
                .ToString("N");

        return engine switch
        {
            "MySql" or "MariaDb" =>
                new MySqlConnectionStringBuilder(server.ConnectionString)
                {
                    Database = databaseName,
                }.ConnectionString,
            "PostgreSql" => new NpgsqlConnectionStringBuilder(server.ConnectionString)
            {
                Database = databaseName,
            }.ConnectionString,
            "SqlServer" => new SqlConnectionStringBuilder(server.ConnectionString)
            {
                InitialCatalog = databaseName,
                MultipleActiveResultSets = false,
            }.ConnectionString,
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };
    }

    /// <summary>Stops every server started by this assembly after all its test collections finish.</summary>
    /// <returns>A task that completes after container cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        var cleanup = new List<Task>();

        foreach (var lazy in _servers.Values)
        {
            if (lazy is { IsValueCreated: true, Value.IsCompletedSuccessfully: true })
            {
                var server = await lazy.Value;
                cleanup.Add(
                    server
                        .Container
                        .DisposeAsync()
                        .AsTask());
            }
        }

        // WHY: One failed cleanup must not prevent the other assembly-owned servers from being disposed.
        await Task.WhenAll(cleanup);
    }

    /// <summary>
    /// Starts one server and releases its container if startup fails before ownership is transferred.
    /// </summary>
    private static async Task<Server> StartAsync(
        string engine
    )
    {
        IContainer container;

        if (engine == "MySql")
        {
            // WHY: Each fixture provisions its own database, beyond the default user's single-database grant.
            container = new MySqlBuilder(DatabaseTestTargets.MySqlImage)
                .WithUsername("root")
                .Build();
        }
        else if (engine == "MariaDb")
        {
            container = new ContainerBuilder(DatabaseTestTargets.MariaDbImage)
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
        }
        else if (engine == "PostgreSql")
        {
            container = new PostgreSqlBuilder(DatabaseTestTargets.PostgreSqlImage).Build();
        }
        else if (engine == "SqlServer")
        {
            container = new MsSqlBuilder(DatabaseTestTargets.SqlServerImage).Build();
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(engine));
        }

        try
        {
            await container.StartAsync(CancellationToken.None);

            var connectionString = engine switch
            {
                "MySql" => ((MySqlContainer)container).GetConnectionString(),
                "PostgreSql" => ((PostgreSqlContainer)container).GetConnectionString(),
                "SqlServer" => ((MsSqlContainer)container).GetConnectionString(),
                "MariaDb" => $"Server={container.Hostname};Port={container.GetMappedPublicPort(3306)};"
                    + "Database=nestedset;User ID=root;Password=nestedset-test-password;",
                _ => throw new ArgumentOutOfRangeException(nameof(engine)),
            };

            return new Server(container, connectionString);
        }
        catch (Exception startupFailure)
        {
            try
            {
                await container.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Database server startup and cleanup both failed.",
                    startupFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>Keeps the container and its connection endpoint under the same assembly lifetime.</summary>
    private sealed record Server(
        IContainer Container,
        string ConnectionString
    );
}
