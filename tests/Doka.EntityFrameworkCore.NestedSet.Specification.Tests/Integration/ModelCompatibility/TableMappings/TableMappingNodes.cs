namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a hierarchy principal sharing its physical row with optional payload metadata.</summary>
internal sealed class TableSplitNode
{
    /// <summary>Gets or sets the stable identity.</summary>
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

/// <summary>Represents optional payload sharing the hierarchy principal's physical table row.</summary>
internal sealed class TableSplitPayload
{
    /// <summary>Gets or sets the identifying shared primary key.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets payload stored outside the hierarchy aggregate.</summary>
    internal string? Description { get; set; }
}

/// <summary>Represents one entity split between payload and hierarchy table fragments.</summary>
internal sealed class EntitySplitNode
{
    /// <summary>Gets or sets the shared fragment primary key and NodeKey.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets payload stored in the primary fragment.</summary>
    internal string Name { get; set; } = "";

    /// <summary>Gets or sets the stable tree identity in the hierarchy fragment.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity in the hierarchy fragment.</summary>
    internal int? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary in the hierarchy fragment.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary in the hierarchy fragment.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth in the hierarchy fragment.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position in the hierarchy fragment.</summary>
    internal long Position { get; set; }
}

/// <summary>Maps two entities to one table through an identifying one-to-one relationship.</summary>
internal sealed class TableSplitContext : DbContext
{
    /// <summary>Creates a table-splitting compatibility context.</summary>
    internal TableSplitContext(
        DbContextOptions<TableSplitContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<TableSplitNode>();
        node.ToTable("TableSplitNodes");
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));

        var payload = modelBuilder.Entity<TableSplitPayload>();
        payload.ToTable("TableSplitNodes");
        payload.HasKey(entity => entity.Id);
        payload.Property(entity => entity.Description);
        payload
            .HasOne<TableSplitNode>()
            .WithOne()
            .HasForeignKey<TableSplitPayload>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps every structural property to one secondary entity-splitting fragment.</summary>
internal sealed class EntitySplitContext : DbContext
{
    /// <summary>Creates an entity-splitting compatibility context.</summary>
    internal EntitySplitContext(
        DbContextOptions<EntitySplitContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<EntitySplitNode>();
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();
        node.ToTable("EntitySplitPayload", fragment => fragment.Property(entity => entity.Name));
        node.SplitToTable(
            "EntitySplitStructure",
            fragment =>
            {
                fragment.Property(entity => entity.TreeId);
                fragment.Property(entity => entity.ParentId);
                fragment.Property(entity => entity.Left);
                fragment.Property(entity => entity.Right);
                fragment.Property(entity => entity.Depth);
                fragment.Property(entity => entity.Position);
            });
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}
