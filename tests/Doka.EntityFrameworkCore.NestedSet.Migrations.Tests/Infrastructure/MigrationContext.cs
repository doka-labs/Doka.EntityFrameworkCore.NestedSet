namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Maps historical and current hierarchy schemas for generated migration tests.</summary>
public sealed class MigrationContext : NestedSetDbContext
{
    /// <summary>The mapped table name used by catalog assertions.</summary>
    public const string TableName = "custom_nested_hierarchy_entries";

    /// <summary>The stable entity-type name shared by the historical and current CLR shapes.</summary>
    private static readonly string s_entityTypeName = typeof(MigrationNode).FullName!;

    /// <summary>Creates a context for one historical model and provider configuration.</summary>
    /// <param name="options">The provider, migration assembly and optional extension configuration.</param>
    /// <param name="stage">The historical model version.</param>
    /// <param name="schema">The explicit server schema, or null for SQLite.</param>
    public MigrationContext(
        DbContextOptions<MigrationContext> options,
        MigrationStage stage,
        string? schema
    ) : base(options)
    {
        Stage = stage;
        Schema = schema;
    }

    /// <summary>Gets the historical model version used by the model-cache key.</summary>
    public MigrationStage Stage { get; }

    /// <summary>Gets the mapped server schema, or null for SQLite.</summary>
    public string? Schema { get; }

    /// <summary>Gets the stable entity-type name used to align historical and current migration metadata.</summary>
    public static string EntityTypeName => s_entityTypeName;

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.HasDefaultSchema(Schema);

        if (Stage == MigrationStage.Baseline)
        {
            ConfigureBaseline(modelBuilder);

            return;
        }

        var node = modelBuilder.Entity<MigrationNode>();

        if (Stage != MigrationStage.Specialized)
        {
            ConfigureRelationalNames(node);
        }
        else
        {
            // WHY: A partial application index cannot replace the unfiltered structural access path.
            node
                .HasIndex(x => new
                {
                    x.Scope,
                    x.TreeId,
                    x.Left,
                })
                .HasFilter("left_bound > 0")
                .HasDatabaseName("application_filtered_left_lookup");
        }

        // WHY: This user-owned index must survive convention reconciliation and generated Down migrations.
        node
            .HasIndex(x => x.Payload)
            .HasDatabaseName("application_payload_lookup");

        node.HasNestedSet(x => x
            .HasNodeKey(n => n.NodeId)
            .HasScope(n => n.Scope)
            .HasTreeId(n => n.TreeId)
            .HasBounds(n => n.Left, n => n.Right)
            .HasParent(n => n.ParentId)
            .HasDepth(n => n.Depth)
            .HasPosition(n => n.Position)
            .OrderBy(n => n.Payload)
            .ThenByDescending(n => n.Category));

        // WHY: Configuring the key last proves that final model conventions observe the completed application model.
        node.HasKey(x => x.NodeId);

        if (Stage == MigrationStage.Specialized)
        {
            ConfigureRelationalNames(node);
        }
    }

    /// <summary>Maps the genuinely historical Int32 schema to the current table and entity identity.</summary>
    private void ConfigureBaseline(
        ModelBuilder modelBuilder
    )
    {
        // WHY: The stable entity name lets EF diff two intentional CLR shapes as one evolving relational entity.
        var node = modelBuilder.SharedTypeEntity<BaselineMigrationNode>(s_entityTypeName);
        node.ToTable(TableName, Schema);
        node.HasKey(x => x.NodeId);
        node.HasAlternateKey(x => new
        {
            x.Scope,
            x.NodeId,
        });

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

        node.HasIndex(x => new
        {
            x.Scope,
            x.Left,
        });

        node.HasIndex(x => new
        {
            x.Scope,
            x.Right,
        });

        node.HasIndex(x => new
        {
            x.Scope,
            x.ParentId,
            x.Position,
        });

        node
            .HasIndex(x => x.Payload)
            .HasDatabaseName("application_payload_lookup");
    }

    /// <summary>Applies custom relational names before or after structural configuration for each scenario.</summary>
    private void ConfigureRelationalNames(
        EntityTypeBuilder<MigrationNode> node
    )
    {
        node.ToTable(TableName, Schema);
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
    }
}
