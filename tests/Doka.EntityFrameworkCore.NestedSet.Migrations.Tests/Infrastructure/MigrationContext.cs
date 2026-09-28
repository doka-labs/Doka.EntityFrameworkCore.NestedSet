namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>The historical and current models used to exercise a complete hierarchy-schema upgrade.</summary>
public enum MigrationStage
{
    /// <summary>The populated schema before Int64 coordinates and tree-scoped access paths were introduced.</summary>
    Baseline,

    /// <summary>The model configured through the current nested-set convention.</summary>
    Current,

    /// <summary>The current model with a user-owned filtered index and late relational name configuration.</summary>
    Specialized,
}

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
                    x.Left
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
            x.NodeId
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
            x.Left
        });
        node.HasIndex(x => new
        {
            x.Scope,
            x.Right
        });
        node.HasIndex(x => new
        {
            x.Scope,
            x.ParentId,
            x.Position
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

/// <summary>Separates historical models and schemas while retaining EF's ordinary model-building services.</summary>
public sealed class MigrationModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(
        DbContext context,
        bool designTime
    )
    {
        var migrationContext = (MigrationContext)context;

        return (context.GetType(), migrationContext.Stage, migrationContext.Schema, designTime);
    }
}

/// <summary>The pre-upgrade persisted hierarchy shape retained for migration compatibility tests.</summary>
public sealed class BaselineMigrationNode
{
    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public int NodeId { get; set; }

    /// <summary>Gets or sets the historical forest identifier.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the stable tree identity retained through the upgrade.</summary>
    public int TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identifier.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the historical Int32 left interval boundary.</summary>
    public int Left { get; set; }

    /// <summary>Gets or sets the historical Int32 right interval boundary.</summary>
    public int Right { get; set; }

    /// <summary>Gets or sets the depth below the root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the historical Int32 sibling position.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets data unrelated to hierarchy structure.</summary>
    public string Payload { get; set; } = "";

    /// <summary>Gets or sets an application-owned secondary sibling-order value.</summary>
    public int Category { get; set; }
}

/// <summary>A current persisted hierarchy row using stable tree identity and Int64 coordinates.</summary>
public sealed class MigrationNode
{
    /// <summary>Gets or sets the application-assigned primary key.</summary>
    public int NodeId { get; set; }

    /// <summary>Gets or sets the application scope that partitions tree identities.</summary>
    public int Scope { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public int TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identifier.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the Int64 left interval boundary.</summary>
    public long Left { get; set; }

    /// <summary>Gets or sets the Int64 right interval boundary.</summary>
    public long Right { get; set; }

    /// <summary>Gets or sets the depth below the root.</summary>
    public int Depth { get; set; }

    /// <summary>Gets or sets the Int64 sibling position.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets data unrelated to the hierarchy's structural coordinates.</summary>
    public string Payload { get; set; } = "";

    /// <summary>Gets or sets an application-owned secondary sibling-order value.</summary>
    public int Category { get; set; }
}
