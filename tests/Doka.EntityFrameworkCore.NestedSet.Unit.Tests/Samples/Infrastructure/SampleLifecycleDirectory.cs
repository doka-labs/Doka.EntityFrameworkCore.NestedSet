namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Owns an isolated temporary directory without changing process-wide sample environment variables.</summary>
internal sealed class SampleLifecycleDirectory : IAsyncDisposable
{
    /// <summary>Gets the unique directory; constructing this owner does not create it.</summary>
    public string DirectoryPath { get; } = Path.Combine(
        Path.GetTempPath(),
        "nestedset-sample-lifecycle",
        Guid.NewGuid().ToString("N"));

    /// <summary>Resolves the chosen sample's fixed filename inside this test-owned directory.</summary>
    /// <param name="sample">The known sample whose database belongs to this test.</param>
    /// <returns>The shared configuration with an explicit isolated directory.</returns>
    public SampleDatabaseConfiguration Configuration(
        string sample = "filesystem"
    ) => SampleDatabaseConfiguration.Create(sample, SampleProvider.Sqlite, sqliteDirectory: DirectoryPath);

    /// <summary>Creates a context using the shared public options path without retaining pooled file handles.</summary>
    /// <param name="configuration">The fixed sample endpoint being tested.</param>
    /// <param name="readOnly">Whether inspection must open an existing file without write access.</param>
    /// <returns>A context that the caller must asynchronously dispose before the directory owner.</returns>
    public static SampleLifecycleContext CreateContext(
        SampleDatabaseConfiguration configuration,
        bool readOnly = false
    )
    {
        var options = configuration.CreateOptions<SampleLifecycleContext>(readOnly);
        var context = new SampleLifecycleContext(options);
        var connection = new SqliteConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString)
        {
            Pooling = false,
        };

        // WHY: Per-test connections close before cleanup; no process-wide pool reset affects sibling tests.
        context.Database.SetConnectionString(connection.ConnectionString);

        return context;
    }

    /// <summary>Arranges a real migrated database and independently seeded ordinary application row.</summary>
    /// <param name="context">The isolated writable fixture context.</param>
    /// <param name="value">The data that later operations must preserve or explicitly reset.</param>
    /// <param name="cancellationToken">The token passed to each database operation.</param>
    /// <returns>A task completing after the migration and application row are persisted.</returns>
    public static async Task SeedAsync(
        SampleLifecycleContext context,
        string value,
        CancellationToken cancellationToken
    )
    {
        var connection = new SqliteConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString);

        // WHY: Directory creation has no async API and is limited to this test's unique database parent.
        Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);
        await context.Database.MigrateAsync(cancellationToken);
        context.Sentinels.Add(
            new SampleLifecycleSentinel
            {
                Id = 1,
                Value = value,
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes only this test's directory after all context-owned file handles have been released.</summary>
    /// <returns>A completed value task after the fixture-owned files have been removed.</returns>
    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(DirectoryPath))
        {
            // WHY: Directory deletion has no async API; this test owner never points at user-supplied storage.
            Directory.Delete(DirectoryPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
