namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Maps a native SQL Server rowversion without substituting an application-managed counter.</summary>
public sealed class RowVersionContext : NestedSetDbContext
{
    /// <summary>Creates the context using the fixture-owned SQL Server connection.</summary>
    public RowVersionContext(
        DbContextOptions<RowVersionContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<SqlServerVersionNode>();
        node.ToTable("VersionNodes", "generated");
        node
            .Property(row => row.Version)
            .IsRowVersion();

        node
            .Property(row => row.Name)
            .HasMaxLength(100);

        node.HasNestedSet(builder => builder
            .HasTreeId(row => row.TreeId)
            .HasScope(row => row.Scope)
            .HasParent(row => row.ParentId)
            .OrderBy(row => row.Name));
    }
}
