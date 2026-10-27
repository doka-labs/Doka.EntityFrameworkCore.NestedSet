namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Maps the optional ancestor access path independently of the historical migration fixture.</summary>
public sealed class AncestorIndexContext : NestedSetDbContext
{
    /// <summary>The generated shadow-property name used throughout the migration scenarios.</summary>
    public const string FlagProperty = "IsBranch";

    /// <summary>The custom physical column name that must survive snapshot generation.</summary>
    public const string FlagColumn = "has_descendants";

    /// <summary>Creates a context for an ordinary or ancestor-index-enabled schema.</summary>
    /// <param name="options">The actual provider and generated migration assembly.</param>
    /// <param name="enabled">Whether the optional access path is configured.</param>
    /// <param name="schema">The physical schema, or null for the provider default.</param>
    /// <param name="adoptIndex">Whether the application owns an equivalent index.</param>
    /// <param name="splitStructure">Whether the hierarchy occupies a secondary split-table fragment.</param>
    public AncestorIndexContext(
        DbContextOptions<AncestorIndexContext> options,
        bool enabled,
        string? schema,
        bool adoptIndex = false,
        bool splitStructure = false
    ) : base(options)
    {
        Enabled = enabled;
        Schema = schema;
        AdoptIndex = adoptIndex;
        SplitStructure = splitStructure;
    }

    /// <summary>Gets whether the optional generated column and index are configured.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the physical server schema.</summary>
    public string? Schema { get; }

    /// <summary>Gets whether the equivalent index belongs to the application.</summary>
    public bool AdoptIndex { get; }

    /// <summary>
    /// Gets whether structural properties occupy a secondary fragment without invalid main-table CHECKs.
    /// </summary>
    public bool SplitStructure { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.HasDefaultSchema(Schema);
        var node = modelBuilder.Entity<MigrationNode>();
        node.ToTable(MigrationContext.TableName, Schema);
        node.HasKey(x => x.NodeId);
        node
            .Property(x => x.NodeId)
            .HasColumnName("entry_id")
            .ValueGeneratedNever();
        node
            .Property(x => x.Scope)
            .HasColumnName("tree_scope");
        node
            .Property(x => x.TreeId)
            .HasColumnName("tree_id");
        node
            .Property(x => x.ParentId)
            .HasColumnName("parent_entry");
        node
            .Property(x => x.Left)
            .HasColumnName("left_bound");
        node
            .Property(x => x.Right)
            .HasColumnName("right_bound");
        node
            .Property(x => x.Depth)
            .HasColumnName("entry_depth");
        node
            .Property(x => x.Position)
            .HasColumnName("sibling_position");
        node
            .Property(x => x.Payload)
            .HasColumnName("entry_payload")
            .HasMaxLength(100);
        node
            .Property(x => x.Category)
            .HasColumnName("entry_category");
        node
            .HasIndex(x => x.Payload)
            .HasDatabaseName("application_payload_lookup");

        if (SplitStructure)
        {
            node.SplitToTable(
                MigrationContext.TableName + "_structure",
                Schema,
                split =>
                {
                    split.Property(x => x.Scope);
                    split.Property(x => x.TreeId);
                    split.Property(x => x.ParentId);
                    split.Property(x => x.Left);
                    split.Property(x => x.Right);
                    split.Property(x => x.Depth);
                    split.Property(x => x.Position);
                });
        }

        if (AdoptIndex)
        {
            // WHY: An explicitly named equivalent application index must be adopted rather than duplicated.
            node.Property<bool>(FlagProperty);
            node
                .HasIndex(
                    nameof(MigrationNode.Scope),
                    FlagProperty,
                    nameof(MigrationNode.TreeId),
                    nameof(MigrationNode.Right),
                    nameof(MigrationNode.Left),
                    nameof(MigrationNode.NodeId))
                .HasDatabaseName("application_ancestor_lookup");
        }

        node.HasNestedSet(builder =>
        {
            builder
                .HasNodeKey(x => x.NodeId)
                .HasScope(x => x.Scope)
                .HasTreeId(x => x.TreeId)
                .HasBounds(x => x.Left, x => x.Right)
                .HasParent(x => x.ParentId)
                .HasDepth(x => x.Depth)
                .HasPosition(x => x.Position);

            if (Enabled)
            {
                builder.HasAncestorIndex(FlagProperty);
            }
        });

        if (Enabled || AdoptIndex)
        {
            // WHY: Late physical naming must be reflected in both the computed SQL and the generated index.
            node
                .Property<bool>(FlagProperty)
                .HasColumnName(FlagColumn);
        }
    }
}

/// <summary>Separates enabled, ordinary, and application-owned migration models.</summary>
public sealed class AncestorIndexModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(
        DbContext context,
        bool designTime
    )
    {
        var model = (AncestorIndexContext)context;

        return (context.GetType(), model.Enabled, model.Schema, model.AdoptIndex, model.SplitStructure, designTime);
    }
}
