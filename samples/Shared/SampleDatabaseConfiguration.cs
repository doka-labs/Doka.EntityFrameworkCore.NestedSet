namespace Doka.EntityFrameworkCore.NestedSet.Samples;

/// <summary>Resolves an endpoint while keeping the database reset target owned by one known sample.</summary>
internal sealed class SampleDatabaseConfiguration
{
    private readonly string _connectionString;

    /// <summary>Constructs an already validated sample endpoint.</summary>
    private SampleDatabaseConfiguration(
        SampleProvider provider,
        string databaseName,
        string connectionString,
        string? sqlitePath,
        string description
    )
    {
        Provider = provider;
        DatabaseName = databaseName;
        _connectionString = connectionString;
        SqlitePath = sqlitePath;
        Description = description;
    }

    /// <summary>Gets the explicit provider route used by runtime and design-time construction.</summary>
    public SampleProvider Provider { get; }

    /// <summary>Gets the fixed project-owned database name, independent of any connection-string override.</summary>
    public string DatabaseName { get; }

    /// <summary>Gets the owned SQLite filename, or null for server databases.</summary>
    public string? SqlitePath { get; }

    /// <summary>Gets a printable endpoint description that does not contain credentials.</summary>
    public string Description { get; }

    /// <summary>Creates a connection configuration for exactly one of the three known projects.</summary>
    /// <param name="sampleName">filesystem, kpis or usergroups.</param>
    /// <param name="provider">The selected documented provider route.</param>
    /// <param name="connectionString">An optional server endpoint override, or null to read the environment.</param>
    /// <param name="sqliteDirectory">An optional directory for the fixed SQLite filename.</param>
    /// <returns>The validated, project-owned connection configuration.</returns>
    /// <exception cref="ArgumentException">The sample, provider or overridden database is invalid.</exception>
    public static SampleDatabaseConfiguration Create(
        string sampleName,
        SampleProvider provider,
        string? connectionString = null,
        string? sqliteDirectory = null
    )
    {
        var databaseName = sampleName switch
        {
            "filesystem" => "nestedset_sample_filesystem",
            "kpis" => "nestedset_sample_kpis",
            "usergroups" => "nestedset_sample_usergroups",
            _ => throw new ArgumentException("Sample must be filesystem, kpis or usergroups.", nameof(sampleName)),
        };

        if (provider == SampleProvider.Sqlite)
        {
            var directory = Path.GetFullPath(
                sqliteDirectory
                ?? Environment.GetEnvironmentVariable("NESTEDSET_SAMPLE_SQLITE_DIRECTORY")
                ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "samples"));

            var sqlitePath = Path.Combine(directory, databaseName + ".sqlite");
            var sqlite = new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                ForeignKeys = true,
            };

            return new SampleDatabaseConfiguration(
                provider,
                databaseName,
                sqlite.ConnectionString,
                sqlitePath,
                $"SQLite: {sqlitePath}");
        }

        var defaults = provider switch
        {
            SampleProvider.MariaDb => "Server=127.0.0.1;Port=33069;User ID=nestedset;Password=nestedset-local-mariadb;",
            SampleProvider.MySql => "Server=127.0.0.1;Port=33068;User ID=nestedset;Password=nestedset-local-mysql;",
            _ => throw new ArgumentException("Unknown sample provider.", nameof(provider)),
        };

        var configured = connectionString
            ?? Environment.GetEnvironmentVariable("NESTEDSET_SAMPLE_CONNECTION_STRING") ?? defaults;

        var builder = new MySqlConnectionStringBuilder(configured);

        // WHY: Reset must never adopt an arbitrary application database from an endpoint override.
        if (builder.Database.Length != 0
            && !string.Equals(builder.Database, databaseName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"This sample requires Database={databaseName}; omit Database from the endpoint override.",
                nameof(connectionString));
        }

        builder.Database = databaseName;

        return new SampleDatabaseConfiguration(
            provider,
            databaseName,
            builder.ConnectionString,
            null,
            $"Doka {provider}: {builder.Server}:{builder.Port}/{databaseName}");
    }

    /// <summary>Builds the same provider and NestedSet registration for runtime and migration factories.</summary>
    /// <typeparam name="TContext">The concrete provider-specific sample context type.</typeparam>
    /// <param name="readOnly">Whether SQLite must open the existing file without write or create access.</param>
    /// <returns>Options that select the explicit provider version and public hierarchy integration.</returns>
    public DbContextOptions<TContext> CreateOptions<TContext>(
        bool readOnly = false
    )
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();

        if (Provider == SampleProvider.Sqlite)
        {
            var sqlite = new SqliteConnectionStringBuilder(_connectionString)
            {
                Mode = readOnly
                    ? SqliteOpenMode.ReadOnly
                    : SqliteOpenMode.ReadWriteCreate,
            };

            options.UseSqlite(sqlite.ConnectionString);
        }
        else
        {
            var version = Provider == SampleProvider.MariaDb
                ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0));

            // WHY: Explicit versions avoid a database connection during help, configuration and design-time discovery.
            options.UseMySql(_connectionString, version);
        }

        options.UseNestedSets();

        return options.Options;
    }
}
