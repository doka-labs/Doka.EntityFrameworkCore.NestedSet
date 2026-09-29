namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Builds SQLite options for EF tooling without touching the database file.</summary>
public sealed class FileSystemSqliteContextFactory : IDesignTimeDbContextFactory<FileSystemSqliteContext>
{
    /// <inheritdoc />
    public FileSystemSqliteContext CreateDbContext(
        string[] args
    )
    {
        ArgumentNullException.ThrowIfNull(args);

        var configuration = SampleDatabaseConfiguration.Create("filesystem", SampleProvider.Sqlite);

        return new FileSystemSqliteContext(configuration.CreateOptions<FileSystemSqliteContext>());
    }
}
