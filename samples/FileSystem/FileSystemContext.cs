namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Exposes an ordinary DbSet and coordinates tracked sorting changes during asynchronous saves.</summary>
public class FileSystemContext : NestedSetDbContext
{
    /// <summary>Creates a context with the caller-selected provider and NestedSet registration.</summary>
    /// <param name="options">The configured EF Core options.</param>
    public FileSystemContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the normal EF Core entity set used for tracked application edits.</summary>
    public DbSet<Folder> Folders => Set<Folder>();

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        // WHY: Persist the sample's comparison policy in migrations so database recreation keeps it after --reset.
        if (Database.ProviderName == "Doka.EntityFrameworkCore.MySql")
        {
            modelBuilder.UseCollation("utf8mb4_bin");
        }

        modelBuilder.ApplyConfiguration(new FolderConfiguration());
    }
}
