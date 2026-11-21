namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps nested-set metadata only on one concrete TPC entity type.</summary>
internal sealed class ConcreteTpcContext : DbContext
{
    /// <summary>Creates a concrete-TPC compatibility context.</summary>
    internal ConcreteTpcContext(
        DbContextOptions<ConcreteTpcContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var root = modelBuilder.Entity<TpcNode>();
        root.UseTpcMappingStrategy();
        root.HasKey(entity => entity.Id);

        var folder = modelBuilder.Entity<TpcFolderNode>();
        folder.ToTable("TpcFolders");
        folder
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        folder.Property(entity => entity.Name);
        folder.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));

        modelBuilder
            .Entity<TpcMetricNode>()
            .ToTable("TpcMetrics");
    }
}
