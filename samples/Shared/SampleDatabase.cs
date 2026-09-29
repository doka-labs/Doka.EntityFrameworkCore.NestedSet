namespace Doka.EntityFrameworkCore.NestedSet.Samples;

/// <summary>Initializes migration-owned sample databases and keeps inspection free of schema writes.</summary>
internal static class SampleDatabase
{
    /// <summary>Prepares a fresh schema or explicitly resets only the configuration's fixed database.</summary>
    /// <param name="context">The provider-specific context created from the owned configuration.</param>
    /// <param name="configuration">The validated project-owned endpoint.</param>
    /// <param name="reset">Whether the caller explicitly requested deletion of this sample's prior results.</param>
    /// <param name="cancellationToken">The cancellation token for all database operations.</param>
    /// <returns>A task that completes when the fresh migration chain has been applied.</returns>
    /// <exception cref="InvalidOperationException">An earlier run exists and no reset was requested.</exception>
    public static async Task PrepareAsync(
        DbContext context,
        SampleDatabaseConfiguration configuration,
        bool reset,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOwnedConnection(context, configuration);

        if (configuration.SqlitePath is { } path)
        {
            // WHY: Directory creation has no async API; it prepares only the fixed sample file's parent directory.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        if (reset)
        {
            await context.Database.EnsureDeletedAsync(cancellationToken);
        }
        else if ((await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Any())
        {
            // WHY: Migration history also detects runs that deleted every node but left TreeId tombstones.
            throw new InvalidOperationException(
                "This sample already has results. Use --inspect to read them, or --reset to start again.");
        }

        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Checks for a previously initialized sample without migrating, seeding or resetting it.</summary>
    /// <param name="context">The context used exclusively for inspection.</param>
    /// <param name="cancellationToken">The cancellation token for the read-only check.</param>
    /// <returns>A task that completes when an initialized database is available.</returns>
    /// <exception cref="InvalidOperationException">There is no initialized result to inspect.</exception>
    public static async Task EnsureAvailableForInspectionAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: CanConnect on a writable SQLite connection may create a file; inspection must fail before opening it.
        if (context.Database.GetDbConnection() is SqliteConnection sqlite
            && !File.Exists(sqlite.DataSource))
        {
            throw new InvalidOperationException("No sample results exist yet. Run a scenario first.");
        }

        if (!await context.Database.CanConnectAsync(cancellationToken)
            || !(await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Any())
        {
            throw new InvalidOperationException("No sample results exist yet. Run a scenario first.");
        }
    }

    /// <summary>Verifies the actual EF connection before any destructive operation can run.</summary>
    private static void ValidateOwnedConnection(
        DbContext context,
        SampleDatabaseConfiguration configuration
    )
    {
        var connection = context.Database.GetDbConnection();

        if (configuration.Provider == SampleProvider.Sqlite)
        {
            var actual = new SqliteConnectionStringBuilder(connection.ConnectionString);

            if (!string.Equals(Path.GetFullPath(actual.DataSource), configuration.SqlitePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The context does not use this sample's owned SQLite file.");
            }
        }
        else
        {
            var actual = new MySqlConnectionStringBuilder(connection.ConnectionString);

            if (!string.Equals(actual.Database, configuration.DatabaseName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The context does not use this sample's owned server database.");
            }
        }
    }
}
