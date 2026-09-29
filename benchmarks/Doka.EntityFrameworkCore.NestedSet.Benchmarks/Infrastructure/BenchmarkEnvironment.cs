namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns database resources in the launcher, outside generated benchmark processes and measurement.</summary>
public sealed class BenchmarkEnvironment : IAsyncDisposable
{
    private const string ConnectionVariable = "NESTEDSET_BENCHMARK_CONNECTION_STRING";
    private const string DatabaseName = "nestedset_benchmarks";
    private readonly string? _previousConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
    private IContainer? _container;
    private string? _databaseFile;
    private bool _disposed;

    /// <summary>Captures ownership and the connection setting to restore when this launch finishes.</summary>
    /// <param name="engine">The selected provider engine.</param>
    private BenchmarkEnvironment(
        BenchmarkEngine engine
    )
    {
        Engine = engine;
    }

    /// <summary>Gets the selected engine.</summary>
    public BenchmarkEngine Engine { get; }

    /// <summary>Gets the immutable container image, or null for an embedded SQLite database.</summary>
    public string? Image { get; private set; }

    /// <summary>Gets the version returned by the running database engine.</summary>
    public string ServerVersion { get; private set; } = string.Empty;

    /// <summary>Gets the database container's configured CPU limit.</summary>
    public const int CpuLimit = 2;

    /// <summary>Gets the database container's configured memory limit in bytes.</summary>
    public const long MemoryLimitBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Gets the resource owner's creation time for provenance.</summary>
    public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the inherited provider connection string without publishing it in results.</summary>
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionVariable)
        ?? (BenchmarkRunOptions.Current.Engine == BenchmarkEngine.SqliteMemory
            ? "Data Source=:memory:;Pooling=False"
            : throw new InvalidOperationException("The launcher has not provisioned the benchmark database."));

    /// <summary>Starts an owned database and publishes its connection after successful version checks.</summary>
    /// <param name="options">Validated provider and run-directory settings.</param>
    /// <param name="cancellationToken">Cancellation for container startup and database checks.</param>
    /// <returns>The resource owner that must outlive all generated benchmark processes.</returns>
    public static async Task<BenchmarkEnvironment> CreateAsync(
        BenchmarkRunOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(options.ArtifactsPath);
        var environment = new BenchmarkEnvironment(options.Engine);

        await BenchmarkLifecycle.InitializeAsync(
            environment,
            async () =>
            {
                var connection = await environment.StartAsync(options.ArtifactsPath, cancellationToken);
                environment.ServerVersion = await ReadServerVersionAsync(options.Engine, connection, cancellationToken);
                Environment.SetEnvironmentVariable(ConnectionVariable, connection);
            });

        return environment;
    }

    /// <summary>Configures the same real provider in the launcher and generated benchmark process.</summary>
    /// <param name="builder">The context options receiving the provider.</param>
    /// <param name="engine">The selected engine.</param>
    /// <param name="connectionString">The inherited connection to the owned database.</param>
    public static void ConfigureProvider(
        DbContextOptionsBuilder builder,
        BenchmarkEngine engine,
        string connectionString
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        switch (engine)
        {
            case BenchmarkEngine.MySql:
                builder.UseMySql(connectionString, DatabaseTestTargets.MySql);
                break;
            case BenchmarkEngine.MariaDb:
                builder.UseMySql(connectionString, DatabaseTestTargets.MariaDb);
                break;
            case BenchmarkEngine.PostgreSql:
                builder.UseNpgsql(connectionString);
                break;
            case BenchmarkEngine.SqlServer:
                builder.UseSqlServer(connectionString);
                break;
            case BenchmarkEngine.SqliteMemory:
            case BenchmarkEngine.SqliteFile:
                builder.UseSqlite(connectionString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionVariable, _previousConnectionString);

            if (_databaseFile is not null)
            {
                File.Delete(_databaseFile);
                File.Delete(_databaseFile + "-wal");
                File.Delete(_databaseFile + "-shm");
                File.Delete(_databaseFile + "-journal");
            }
        }
    }

    /// <summary>Starts the selected owned server or SQLite file with fixed resources and disabled pooling.</summary>
    /// <param name="artifactsPath">The absolute run directory that owns any SQLite file.</param>
    /// <param name="cancellationToken">Cancellation for server startup.</param>
    /// <returns>The private connection configuration to publish after successful initialization.</returns>
    private async Task<string> StartAsync(
        string artifactsPath,
        CancellationToken cancellationToken
    )
    {
        var password = "Nset!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        switch (Engine)
        {
            case BenchmarkEngine.MySql:
                Image = DatabaseTestTargets.MySqlImage;
                var mysql = new MySqlBuilder(Image)
                    .WithDatabase(DatabaseName)
                    .WithUsername("benchmark")
                    .WithPassword(password)
                    .WithLogger(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
                    // WHY: Testcontainers exposes Docker CPU and memory limits through this vendor seam.
                    .WithCreateParameterModifier(parameters =>
                    {
                        var configuration = parameters.HostConfig
                            ?? throw new InvalidOperationException("Testcontainers omitted the host configuration.");

                        configuration.NanoCPUs = CpuLimit * 1_000_000_000L;
                        configuration.Memory = MemoryLimitBytes;
                    })
                    .Build();

                _container = mysql;
                await mysql.StartAsync(cancellationToken);

                return new MySqlConnectionStringBuilder(mysql.GetConnectionString())
                {
                    Pooling = false,
                    AutoEnlist = false,
                }.ConnectionString;

            case BenchmarkEngine.MariaDb:
                Image = DatabaseTestTargets.MariaDbImage;
                var maria = new ContainerBuilder(Image)
                    .WithEnvironment("MARIADB_ROOT_PASSWORD", password)
                    .WithEnvironment("MARIADB_DATABASE", DatabaseName)
                    .WithPortBinding(3306, true)
                    .WithLogger(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
                    // WHY: This MariaDB image ships native client names, while MySqlBuilder waits for mysql.
                    .WithWaitStrategy(
                        Wait
                            .ForUnixContainer()
                            .UntilCommandIsCompleted(
                                "mariadb --protocol=tcp -h127.0.0.1 -uroot "
                                + "-p\"$MARIADB_ROOT_PASSWORD\" -e \"SELECT 1\"",
                                wait => wait.WithTimeout(TimeSpan.FromMinutes(2))))
                    .WithCreateParameterModifier(parameters =>
                    {
                        var configuration = parameters.HostConfig
                            ?? throw new InvalidOperationException("Testcontainers omitted the host configuration.");

                        configuration.NanoCPUs = CpuLimit * 1_000_000_000L;
                        configuration.Memory = MemoryLimitBytes;
                    })
                    .Build();

                _container = maria;
                await maria.StartAsync(cancellationToken);

                return new MySqlConnectionStringBuilder
                {
                    Server = maria.Hostname,
                    Port = maria.GetMappedPublicPort(3306),
                    Database = DatabaseName,
                    UserID = "root",
                    Password = password,
                    Pooling = false,
                    AutoEnlist = false,
                }.ConnectionString;

            case BenchmarkEngine.PostgreSql:
                Image = DatabaseTestTargets.PostgreSqlImage;
                var postgres = new PostgreSqlBuilder(Image)
                    .WithDatabase(DatabaseName)
                    .WithUsername("benchmark")
                    .WithPassword(password)
                    .WithLogger(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
                    .WithCreateParameterModifier(parameters =>
                    {
                        var configuration = parameters.HostConfig
                            ?? throw new InvalidOperationException("Testcontainers omitted the host configuration.");

                        configuration.NanoCPUs = CpuLimit * 1_000_000_000L;
                        configuration.Memory = MemoryLimitBytes;
                    })
                    .Build();

                _container = postgres;
                await postgres.StartAsync(cancellationToken);

                return new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
                {
                    Pooling = false,
                    Enlist = false,
                }.ConnectionString;

            case BenchmarkEngine.SqlServer:
                Image = DatabaseTestTargets.SqlServerImage;
                var sqlServer = new MsSqlBuilder(Image)
                    .WithDatabase(DatabaseName)
                    .WithPassword(password)
                    .WithLogger(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
                    .WithCreateParameterModifier(parameters =>
                    {
                        var configuration = parameters.HostConfig
                            ?? throw new InvalidOperationException("Testcontainers omitted the host configuration.");

                        configuration.NanoCPUs = CpuLimit * 1_000_000_000L;
                        configuration.Memory = MemoryLimitBytes;
                    })
                    .Build();

                _container = sqlServer;
                await sqlServer.StartAsync(cancellationToken);

                return new SqlConnectionStringBuilder(sqlServer.GetConnectionString())
                {
                    Pooling = false,
                    Enlist = false,
                    TrustServerCertificate = true,
                }.ConnectionString;

            case BenchmarkEngine.SqliteMemory:
                return "Data Source=:memory:;Pooling=False";

            case BenchmarkEngine.SqliteFile:
                _databaseFile = Path.Combine(artifactsPath, $"database-{Guid.NewGuid():N}.sqlite");

                return new SqliteConnectionStringBuilder
                {
                    DataSource = _databaseFile,
                    Pooling = false,
                }.ConnectionString;

            default:
                throw new InvalidOperationException("The benchmark engine is invalid.");
        }
    }

    /// <summary>Checks connectivity and captures the actual version independently of capability metadata.</summary>
    /// <param name="engine">The selected database engine.</param>
    /// <param name="connectionString">The unpublished connection to the owned resource.</param>
    /// <param name="cancellationToken">Cancellation for connection and version IO.</param>
    /// <returns>The version reported by the database engine.</returns>
    private static async Task<string> ReadServerVersionAsync(
        BenchmarkEngine engine,
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using DbConnection connection = engine switch
        {
            BenchmarkEngine.MySql or BenchmarkEngine.MariaDb => new MySqlConnection(connectionString),
            BenchmarkEngine.PostgreSql => new NpgsqlConnection(connectionString),
            BenchmarkEngine.SqlServer => new SqlConnection(connectionString),
            BenchmarkEngine.SqliteMemory or BenchmarkEngine.SqliteFile => new SqliteConnection(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = engine switch
        {
            BenchmarkEngine.MySql or BenchmarkEngine.MariaDb => "SELECT VERSION();",
            BenchmarkEngine.PostgreSql => "SHOW server_version;",
            BenchmarkEngine.SqlServer => "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion'));",
            BenchmarkEngine.SqliteMemory or BenchmarkEngine.SqliteFile => "SELECT sqlite_version();",
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The database did not return its server version.");
    }
}
