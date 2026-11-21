namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps one invalid polymorphic nested set across all concrete TPC tables.</summary>
internal sealed class PolymorphicTpcContext : DbContext
{
    /// <summary>Creates an invalid polymorphic-TPC context.</summary>
    internal PolymorphicTpcContext(
        DbContextOptions<PolymorphicTpcContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var root = modelBuilder.Entity<TpcNode>();
        root.UseTpcMappingStrategy();
        root.HasKey(entity => entity.Id);
        root
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        root.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));

        modelBuilder
            .Entity<TpcFolderNode>()
            .ToTable("InvalidTpcFolders");

        modelBuilder
            .Entity<TpcMetricNode>()
            .ToTable("InvalidTpcMetrics");
    }
}
