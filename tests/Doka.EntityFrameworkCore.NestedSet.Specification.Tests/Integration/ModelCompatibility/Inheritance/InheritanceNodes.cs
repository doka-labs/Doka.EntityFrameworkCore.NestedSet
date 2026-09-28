namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides structural state shared by the inheritance compatibility models.</summary>
internal abstract class InheritanceNode
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

/// <summary>Represents a folder in a TPH hierarchy.</summary>
internal sealed class TphFolderNode : InheritanceNode
{
    /// <summary>Gets or sets folder-specific payload.</summary>
    internal string FolderKind { get; set; } = "";
}

/// <summary>Represents a metric in a TPH hierarchy.</summary>
internal sealed class TphMetricNode : InheritanceNode
{
    /// <summary>Gets or sets metric-specific payload.</summary>
    internal decimal Target { get; set; }
}

/// <summary>Represents a folder in a TPT hierarchy.</summary>
internal sealed class TptFolderNode : InheritanceNode
{
    /// <summary>Gets or sets folder-specific payload.</summary>
    internal string FolderKind { get; set; } = "";
}

/// <summary>Represents a metric stored in another TPT derived table.</summary>
internal sealed class TptMetricNode : InheritanceNode
{
    /// <summary>Gets or sets metric-specific payload.</summary>
    internal string MetricKind { get; set; } = "";
}

/// <summary>Maps a complete polymorphic tree through one TPH table.</summary>
internal sealed class TphContext : DbContext
{
    /// <summary>Creates a TPH compatibility context.</summary>
    internal TphContext(
        DbContextOptions<TphContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<InheritanceNode>();
        node.ToTable("TphNodes");
        node
            .HasDiscriminator<string>("NodeType")
            .HasValue<TphFolderNode>("Folder")
            .HasValue<TphMetricNode>("Metric");
        Configure(node);
    }

    /// <summary>Applies the structural base mapping used by the inheritance tests.</summary>
    internal static void Configure(
        EntityTypeBuilder<InheritanceNode> node
    )
    {
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();
        node.Property(entity => entity.Name);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}

/// <summary>Maps structural columns to one base table and derived payload to a TPT table.</summary>
internal sealed class TptContext : DbContext
{
    /// <summary>Creates a TPT compatibility context.</summary>
    internal TptContext(
        DbContextOptions<TptContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<InheritanceNode>();
        node.UseTptMappingStrategy();
        node.ToTable("TptNodes");
        modelBuilder
            .Entity<TptFolderNode>()
            .ToTable("TptFolders")
            .Property(entity => entity.FolderKind);

        modelBuilder
            .Entity<TptMetricNode>()
            .ToTable("TptMetrics")
            .Property(entity => entity.MetricKind);

        TphContext.Configure(node);
    }
}
