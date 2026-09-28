namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides inherited scalar properties for independently mapped concrete TPC hierarchies.</summary>
internal abstract class TpcNode
{
    /// <summary>Gets or sets the stable node identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application payload.</summary>
    internal string Name { get; set; } = "";
}

/// <summary>Represents one concrete TPC hierarchy stored in its own table.</summary>
internal sealed class TpcFolderNode : TpcNode;

/// <summary>Represents another concrete TPC entity table outside the folder hierarchy.</summary>
internal sealed class TpcMetricNode : TpcNode;

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
