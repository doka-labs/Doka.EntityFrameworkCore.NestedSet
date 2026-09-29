namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Selects the SQLite migration history while sharing the same application model.</summary>
public sealed class FileSystemSqliteContext : FileSystemContext
{
    /// <summary>Creates the optional local SQLite context.</summary>
    /// <param name="options">The SQLite provider and NestedSet options.</param>
    public FileSystemSqliteContext(
        DbContextOptions<FileSystemSqliteContext> options
    ) : base(options) { }
}
