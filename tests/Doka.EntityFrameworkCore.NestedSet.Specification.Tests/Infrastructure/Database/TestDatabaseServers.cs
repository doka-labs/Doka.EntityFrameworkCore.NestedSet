using System.Collections.Concurrent;
using MySqlConnector;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares one container per server engine across an integration-test assembly.</summary>
/// <remarks>Each caller receives a distinct database, so xUnit collections can run concurrently.</remarks>
public sealed class TestDatabaseServers : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Startup> _servers = new(
        StringComparer.Ordinal);

    private readonly Func<string, CancellationToken, Task<Server>> _start;

    /// <summary>Uses native containers for the executing assembly's shared server lifetime.</summary>
    public TestDatabaseServers() : this(StartAsync) { }

    /// <summary>Permits deterministic startup and teardown races without starting a real container.</summary>
    /// <param name="start">The startup owner, including its failure cleanup, receiving the caller token.</param>
    internal TestDatabaseServers(
        Func<string, CancellationToken, Task<Server>> start
    )
    {
        _start = start;
    }

    /// <summary>Stops every server started by this assembly after all its test collections finish.</summary>
    /// <returns>A task that completes after container cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        var cleanup = new List<Task>();

        foreach (var startup in _servers.Values)
        {
            if (startup.Task.IsValueCreated)
            {
                cleanup.Add(DisposeServerAsync(startup.Task.Value));
            }
        }

        // WHY: One failed cleanup must not prevent the other assembly-owned servers from being disposed.
        await Task.WhenAll(cleanup);
    }

    /// <summary>Resolves the server owner of the currently executing provider test assembly.</summary>
    /// <param name="cancellationToken">The caller's token checked before framework fixture lookup.</param>
    /// <returns>The fixture registered by that executable assembly.</returns>
    /// <remarks>Call from a class fixture or test, where xUnit exposes fixture mappings.</remarks>
    internal static async ValueTask<TestDatabaseServers> GetCurrentAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: The specification library can serve multiple test assemblies in one process; a static owner
        // would couple their container lifetimes and allow one assembly to dispose another's servers.
        var fixture = await TestContext.Current.GetFixture<TestDatabaseServers>();

        return fixture
            ?? throw new InvalidOperationException("The current test assembly has no database-server fixture.");
    }

    /// <summary>Creates a connection string for a new database on the shared server.</summary>
    /// <param name="engine">MySql, MariaDb, PostgreSQL, or SqlServer.</param>
    /// <param name="cancellationToken">The caller's token for server acquisition.</param>
    /// <returns>A connection string with a database name unique to this caller.</returns>
    internal async Task<string> NewConnectionStringAsync(
        string engine,
        CancellationToken cancellationToken
    )
    {
        if (!ProviderEngineOwnership.Includes(engine))
        {
            throw new InvalidOperationException($"The current test assembly does not own the {engine} server.");
        }

        Server server;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: Lazy gives one caller ownership of startup. Every waiter observes terminal startup before
            // continuing, so cancellation cannot leave an active command racing fixture disposal.
            var pending = _servers.GetOrAdd(
                engine,
                static (name, state) => new Startup(
                    new Lazy<Task<Server>>(() => state.Start(name, state.Token)),
                    state.Token),
                (Start: _start, Token: cancellationToken));

            try
            {
                server = await pending.Task.Value;
            }
            catch (OperationCanceledException) when (pending.Owner.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                // WHY: Another collection can cancel the shared first attempt. Its startup cleanup has
                // completed before this retry; a live waiter must not inherit that collection's cancellation.
                _servers.TryRemove(new KeyValuePair<string, Startup>(engine, pending));

                continue;
            }
            catch
            {
                // WHY: Failed startup owns and releases its container. Eviction permits a later independent
                // acquisition without leaving a permanently canceled or faulted task in the fixture.
                _servers.TryRemove(new KeyValuePair<string, Startup>(engine, pending));

                throw;
            }

            cancellationToken.ThrowIfCancellationRequested();

            break;
        }

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

    /// <summary>Observes startup to completion before releasing its server without caller cancellation.</summary>
    private static async Task DisposeServerAsync(
        Task<Server> startup
    )
    {
        Server server;

        try
        {
            server = await startup;
        }
        catch
        {
            // WHY: StartAsync already disposes a failed startup and preserves its error for its caller.
            return;
        }

        await server.Container.DisposeAsync();
    }

    /// <summary>
    /// Starts one server and releases its container if startup fails before ownership is transferred.
    /// </summary>
    /// <param name="engine">The selected provider engine.</param>
    /// <param name="cancellationToken">The startup owner's token, never used for failure cleanup.</param>
    private static async Task<Server> StartAsync(
        string engine,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        SqlServerTestPlatform.RequireSupportedContainerHost(engine);

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
            await container.StartAsync(cancellationToken);

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
    internal sealed record Server(
        IAsyncDisposable Container,
        string ConnectionString
    );

    /// <summary>Retains proof that a shared startup was canceled by its owning caller rather than the provider.</summary>
    private readonly record struct Startup(
        Lazy<Task<Server>> Task,
        CancellationToken Owner
    );
}
