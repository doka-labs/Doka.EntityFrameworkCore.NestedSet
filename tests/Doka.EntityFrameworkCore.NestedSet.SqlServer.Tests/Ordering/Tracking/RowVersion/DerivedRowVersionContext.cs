namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Maps a mixed TPH hierarchy whose generated concurrency token belongs only to one subtype.</summary>
/// <param name="options">The provider options configured by the isolated fixture.</param>
public sealed class DerivedRowVersionContext : DbContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public DerivedRowVersionContext(
        DbContextOptions<DerivedRowVersionContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<DerivedVersionBaseNode>();
        node.ToTable("DerivedVersionNodes", "generated");
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node
            .Property(value => value.Name)
            .HasMaxLength(100);

        node
            .Property(value => value.Payload)
            .HasMaxLength(100);

        node.HasNestedSet(builder => builder
            .HasScope(value => value.Scope)
            .HasTreeId(value => value.TreeId)
            .HasParent(value => value.ParentId)
            .OrderBy(value => value.Name));

        modelBuilder
            .Entity<DerivedVersionTokenNode>()
            .Property(value => value.Version)
            .IsRowVersion();

        modelBuilder.Entity<DerivedVersionPlainNode>();
    }
}
