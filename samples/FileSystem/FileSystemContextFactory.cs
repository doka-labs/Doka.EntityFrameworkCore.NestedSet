namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Builds Doka options for EF tooling without creating, migrating, or resetting a database.</summary>
public sealed class FileSystemContextFactory : IDesignTimeDbContextFactory<FileSystemContext>
{
    /// <inheritdoc />
    public FileSystemContext CreateDbContext(
        string[] args
    )
    {
        var command = SampleCommand.Parse(args, Program.Scenarios);

        if (command.Provider == SampleProvider.Sqlite)
        {
            throw new ArgumentException("Use FileSystemSqliteContext for SQLite migrations.", nameof(args));
        }

        var configuration = SampleDatabaseConfiguration.Create("filesystem", command.Provider);

        return new FileSystemContext(configuration.CreateOptions<FileSystemContext>());
    }
}
