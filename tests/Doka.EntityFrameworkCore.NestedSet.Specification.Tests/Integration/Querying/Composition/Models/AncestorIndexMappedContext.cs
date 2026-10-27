namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies the optional physical index against the existing supported mapping entity shapes.</summary>
/// <param name="options">The fixture-owned provider options.</param>
internal sealed partial class AncestorIndexMappedContext(DbContextOptions<AncestorIndexMappedContext> options) : DbContext(options)
{
    /// <summary>Names the selected property-bag hierarchy independently of its shared CLR type.</summary>
    internal const string First = "AncestorIndexFirstBag";

    /// <summary>Names a distinct physical property-bag mapping with deliberately colliding identities.</summary>
    internal const string Second = "AncestorIndexSecondBag";

    /// <summary>Names ordinary consumer data that is independent of generated and writable hierarchy roles.</summary>
    internal const string Payload = "Label";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureShadow(modelBuilder);
        ConfigureConverted(modelBuilder);
        ConfigureBinary(modelBuilder);
        ConfigureAlternateKey(modelBuilder);
        ConfigureBroadTree(modelBuilder);
        ConfigureFields(modelBuilder);
        ConfigureNamed(modelBuilder, First);
        ConfigureNamed(modelBuilder, Second);
        ConfigureInheritance(modelBuilder, tpt: false);
        ConfigureTpc(modelBuilder);
        ConfigureTableSplit(modelBuilder);
        ConfigureEntitySplit(modelBuilder);
    }

    /// <summary>Maps conventional scalar roles without bypassing the public configuration contract.</summary>
    private static void Configure<TEntity>(EntityTypeBuilder<TEntity> node, string? scope = null)
        where TEntity : class
    {
        // WHY: The reused fixture entities expose internal properties, which EF does not discover
        // by convention. String role selection intentionally requires already mapped properties.
        if (node.Metadata.FindProperty("TreeId") is null)
        {
            node.Property<Guid>("TreeId");
        }

        if (node.Metadata.FindProperty("ParentId") is null)
        {
            node.Property<int?>("ParentId");
        }

        node.Property<long>("Left");
        node.Property<long>("Right");
        node.Property<int>("Depth");
        node.Property<long>("Position");
        node.HasNestedSet(mapping =>
        {
            mapping.HasNodeKey("Id").HasTreeId("TreeId").HasParent("ParentId")
                .HasBounds("Left", "Right").HasDepth("Depth").HasPosition("Position").HasAncestorIndex();

            if (scope is not null)
            {
                mapping.HasScope(scope);
            }
        });
    }

    /// <summary>Keeps every structural value shadow-backed and every physical name independent of the role name.</summary>
    private static void ConfigureShadow(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<CompositionShadowNode>();
        node.ToTable("AncestorIndexShadowNodes");
        node.Ignore(entity => entity.Left);
        node.Ignore(entity => entity.Right);
        node.Ignore(entity => entity.Depth);
        node.Property(entity => entity.Label).HasMaxLength(32);
        node.Property<Guid>(CompositionShadowContext.Scope);
        node.Property<int>(CompositionShadowContext.Key).ValueGeneratedNever();
        node.Property<Guid>(CompositionShadowContext.Tree);
        node.Property<int?>(CompositionShadowContext.Parent);
        node.Property<long>(CompositionShadowContext.Start).HasColumnName("StartCoordinate");
        node.Property<long>(CompositionShadowContext.End).HasColumnName("EndCoordinate");
        node.Property<int>(CompositionShadowContext.Depth);
        node.Property<long>(CompositionShadowContext.Position);
        node.HasKey(CompositionShadowContext.Scope, CompositionShadowContext.Key);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(CompositionShadowContext.Key).HasScope(CompositionShadowContext.Scope)
            .HasTreeId(CompositionShadowContext.Tree).HasParent(CompositionShadowContext.Parent)
            .HasBounds(CompositionShadowContext.Start, CompositionShadowContext.End)
            .HasDepth(CompositionShadowContext.Depth).HasPosition(CompositionShadowContext.Position)
            .HasAncestorIndex("IndexedOwner"));
    }

    /// <summary>Retains operator-free native key conversion and its nullable parent representation.</summary>
    private static void ConfigureConverted(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<CompositionConvertedNode>();
        node.ToTable("AncestorIndexConvertedNodes");
        node.Property(entity => entity.Id)
            .HasConversion(key => key.Value, value => new CompositionNodeKey(value)).ValueGeneratedNever();
        node.Property(entity => entity.ParentId).HasConversion(
            key => key.HasValue ? key.Value.Value : (int?)null,
            value => value.HasValue ? new CompositionNodeKey(value.Value) : null);
        node.HasKey(entity => new { entity.Scope, entity.Id });
        Configure(node, nameof(CompositionConvertedNode.Scope));
    }

    /// <summary>Maps raw fields whose public structural getters deliberately return decorated values.</summary>
    private static void ConfigureFields(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<FieldNode>();
        node.ToTable("AncestorIndexFieldNodes");
        node.Property(entity => entity.Id).ValueGeneratedNever();
        node.Property(entity => entity.Left).HasField("_left").UsePropertyAccessMode(PropertyAccessMode.Field);
        node.Property(entity => entity.Right).HasField("_right").UsePropertyAccessMode(PropertyAccessMode.Field);
        node.Property(entity => entity.Depth).HasField("_depth").UsePropertyAccessMode(PropertyAccessMode.Field);
        Configure(node, nameof(FieldNode.Tree));
    }

    /// <summary>Creates independently identified public shared-type mappings with colliding keys and coordinates.</summary>
    private static void ConfigureNamed(ModelBuilder modelBuilder, string name)
    {
        var node = modelBuilder.SharedTypeEntity<Dictionary<string, object?>>(name);
        node.ToTable(name);
        node.IndexerProperty<int>(NestedSetPropertyRoles.NodeKey).ValueGeneratedNever();
        node.IndexerProperty<Guid>(NestedSetPropertyRoles.TreeId);
        node.IndexerProperty<int?>(NestedSetPropertyRoles.Parent);
        node.IndexerProperty<long>(NestedSetPropertyRoles.Left);
        node.IndexerProperty<long>(NestedSetPropertyRoles.Right);
        node.IndexerProperty<int>(NestedSetPropertyRoles.Depth);
        node.IndexerProperty<long>(NestedSetPropertyRoles.Position);
        node.IndexerProperty<string?>(Payload).HasMaxLength(80);
        node.HasKey(NestedSetPropertyRoles.NodeKey);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(NestedSetPropertyRoles.NodeKey).HasTreeId(NestedSetPropertyRoles.TreeId)
            .HasParent(NestedSetPropertyRoles.Parent).HasBounds(NestedSetPropertyRoles.Left, NestedSetPropertyRoles.Right)
            .HasDepth(NestedSetPropertyRoles.Depth).HasPosition(NestedSetPropertyRoles.Position).HasAncestorIndex());
    }

    /// <summary>Maps the same structural owner through TPH or TPT with an explicit unambiguous parent relationship.</summary>
    internal static void ConfigureInheritance(ModelBuilder modelBuilder, bool tpt)
    {
        var node = modelBuilder.Entity<InheritanceNode>();
        node.HasKey(entity => entity.Id);
        node.Property(entity => entity.Id).ValueGeneratedNever();
        node.Property(entity => entity.Name).HasMaxLength(80);

        if (tpt)
        {
            node.UseTptMappingStrategy();
            node.ToTable("AncestorIndexTptNodes");
            modelBuilder.Entity<TptFolderNode>().ToTable("AncestorIndexTptFolders")
                .Property(entity => entity.FolderKind).HasMaxLength(80);
            modelBuilder.Entity<TptMetricNode>().ToTable("AncestorIndexTptMetrics")
                .Property(entity => entity.MetricKind).HasMaxLength(80);
        }
        else
        {
            node.ToTable("AncestorIndexTphNodes");
            node.HasDiscriminator<string>("NodeType")
                .HasValue<TphFolderNode>("Folder").HasValue<TphMetricNode>("Metric");
            modelBuilder.Entity<TphFolderNode>().Property(entity => entity.FolderKind).HasMaxLength(80);
            modelBuilder.Entity<TphMetricNode>().Property(entity => entity.Target);
        }

        Configure(node);
        // WHY: EF's TPT inheritance key foreign keys are unrelated to hierarchy adjacency.
        // An explicit public relationship keeps ParentId selection unambiguous on the base owner.
        node.HasOne<InheritanceNode>().WithMany().HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>Maps only one concrete TPC table as a hierarchy without introducing a polymorphic owner.</summary>
    private static void ConfigureTpc(ModelBuilder modelBuilder)
    {
        var root = modelBuilder.Entity<TpcNode>();
        root.UseTpcMappingStrategy();
        root.HasKey(entity => entity.Id);
        root.Property(entity => entity.Id).ValueGeneratedNever();
        var node = modelBuilder.Entity<TpcFolderNode>();
        node.ToTable("AncestorIndexTpcFolders");
        node.Property(entity => entity.Name).HasMaxLength(80);
        Configure(node);
        modelBuilder.Entity<TpcMetricNode>().ToTable("AncestorIndexTpcMetrics");
    }

    /// <summary>Shares one principal table row with independently mapped optional application payload.</summary>
    private static void ConfigureTableSplit(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<TableSplitNode>();
        node.ToTable("AncestorIndexTableSplitNodes");
        node.Property(entity => entity.Id).ValueGeneratedNever();
        node.Property(entity => entity.Name).HasMaxLength(80);
        Configure(node);
        var payload = modelBuilder.Entity<TableSplitPayload>();
        payload.ToTable("AncestorIndexTableSplitNodes");
        payload.HasKey(entity => entity.Id);
        payload.Property(entity => entity.Description).HasMaxLength(80);
        payload.HasOne<TableSplitNode>().WithOne().HasForeignKey<TableSplitPayload>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>Places every structural role in a secondary fragment, leaving application payload in its primary table.</summary>
    private static void ConfigureEntitySplit(ModelBuilder modelBuilder)
    {
        var node = modelBuilder.Entity<EntitySplitNode>();
        node.Property(entity => entity.Id).ValueGeneratedNever();
        node.Property(entity => entity.Name).HasMaxLength(80);
        node.ToTable("AncestorIndexEntityPayload", fragment => fragment.Property(entity => entity.Name));
        node.SplitToTable("AncestorIndexEntityStructure", fragment =>
        {
            fragment.Property(entity => entity.TreeId);
            fragment.Property(entity => entity.ParentId);
            fragment.Property(entity => entity.Left);
            fragment.Property(entity => entity.Right);
            fragment.Property(entity => entity.Depth);
            fragment.Property(entity => entity.Position);
        });

        Configure(node);
    }
}
