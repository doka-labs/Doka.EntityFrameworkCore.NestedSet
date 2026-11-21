using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Maps independent native key tables without replacing actual provider conventions.</summary>
internal abstract class NativeGuardContext : DbContext
{
    /// <summary>Uses the provider and database owned by the current benchmark child.</summary>
    internal NativeGuardContext(DbContextOptions options) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        switch (this)
        {
            case IntegerGuardContext:
                // WHY: The reference TreeId needs its converter before name-based role selection can find it.
                ConvertTree(modelBuilder, modelBuilder.Entity<IntegerGuardNode>());
                Configure(modelBuilder.Entity<IntegerGuardNode>());
                break;
            case GuidGuardContext:
                Configure(modelBuilder.Entity<GuidGuardNode>());
                break;
            case ConvertedIntegerGuardContext:
                // WHY: The reference TreeId needs its converter before name-based role selection can find it.
                ConvertTree(modelBuilder, modelBuilder.Entity<ConvertedIntegerGuardNode>());
                Configure(modelBuilder.Entity<ConvertedIntegerGuardNode>());
                modelBuilder.Entity<ConvertedIntegerGuardNode>()
                    .Property(node => node.Id).HasConversion<string>().HasMaxLength(20);
                modelBuilder.Entity<ConvertedIntegerGuardNode>()
                    .Property(node => node.ParentId).HasConversion<string>().HasMaxLength(20);
                break;
            case ConvertedTreeGuardContext:
                // WHY: The reference TreeId needs its converter before name-based role selection can find it.
                ConvertTree(modelBuilder, modelBuilder.Entity<ConvertedTreeGuardNode>());
                Configure(modelBuilder.Entity<ConvertedTreeGuardNode>());
                modelBuilder.Entity<ConvertedTreeGuardNode>()
                    .Property(node => node.Id).HasConversion<string>().HasMaxLength(20);
                modelBuilder.Entity<ConvertedTreeGuardNode>()
                    .Property(node => node.ParentId).HasConversion<string>().HasMaxLength(20);
                break;
            case StringGuardContext:
                Configure(modelBuilder.Entity<StringGuardNode>());
                var collation = Database.IsSqlite() ? "BINARY"
                    : Database.IsSqlServer() ? "Latin1_General_100_BIN2"
                    : Database.IsNpgsql() ? "C" : "utf8mb4_bin";
                var scalar = modelBuilder.Entity<StringGuardNode>();
                scalar.Property(node => node.Id).HasMaxLength(40).UseCollation(collation);
                scalar.Property(node => node.ParentId).HasMaxLength(40).UseCollation(collation);
                break;
            default:
                throw new InvalidOperationException("The native guard context requires an exact key mapping.");
        }

    }

    /// <summary>Retains scalar-converted tree equality independently of the node-key transport.</summary>
    private void ConvertTree<TEntity>(
        ModelBuilder modelBuilder,
        EntityTypeBuilder<TEntity> entity
    ) where TEntity : class
    {
        var tree = entity.Property<NativeGuardTreeId>("TreeId").HasConversion(value => value.Value,
            value => new NativeGuardTreeId(value)).HasMaxLength(40);
        var collation = Database.IsSqlite() ? "NOCASE"
            : Database.IsSqlServer() ? "Latin1_General_100_CI_AS"
            : Database.IsNpgsql() ? "native_guard_tree_ci" : "utf8mb4_general_ci";

        if (Database.IsNpgsql())
        {
            modelBuilder.HasCollation("native_guard_tree_ci", locale: "und-u-ks-level2",
                provider: "icu", deterministic: false);
        }

        tree.UseCollation(collation);
        tree.Metadata.SetValueComparer(new ValueComparer<NativeGuardTreeId>(
            (first, second) => StringComparer.Ordinal.Equals(first!.Value, second!.Value),
            value => StringComparer.Ordinal.GetHashCode(value.Value),
            value => new NativeGuardTreeId(value.Value)));
    }

    /// <summary>Defines the same assigned-key and complete structural roles for each physical key shape.</summary>
    private static void Configure<TEntity>(EntityTypeBuilder<TEntity> entity) where TEntity : class
    {
        entity.HasKey("Id");
        entity.Property("Id").ValueGeneratedNever();
        entity.Property<string>("Name").HasMaxLength(100);
        entity.HasOne<TEntity>().WithMany().HasForeignKey("ParentId").OnDelete(DeleteBehavior.Restrict);
        entity.HasNestedSet(mapping => mapping.HasNodeKey("Id").HasTreeId("TreeId")
            .HasParent("ParentId").HasBounds("Left", "Right").HasDepth("Depth").HasPosition("Position"));
    }
}
