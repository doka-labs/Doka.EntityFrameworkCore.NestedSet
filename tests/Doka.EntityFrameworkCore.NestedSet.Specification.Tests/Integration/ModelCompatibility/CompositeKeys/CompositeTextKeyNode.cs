namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a scoped hierarchy whose text NodeKey is unique only within its tenant.</summary>
internal sealed class CompositeTextKeyNode
{
    /// <summary>Gets or sets the tenant component of the relational identity.</summary>
    internal int TenantId { get; set; }

    /// <summary>Gets or sets the tenant-local text hierarchy identity.</summary>
    internal string NodeKey { get; set; } = "";

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent NodeKey within the same tenant.</summary>
    internal string? ParentNodeKey { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the zero-based depth.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the dense sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets the configured sibling ordering value.</summary>
    internal string Name { get; set; } = "";
}

/// <summary>Maps a text NodeKey that needs ordinal rowsets and repeats across tenants.</summary>
internal sealed class CompositeTextKeyContext : DbContext
{
    /// <summary>Creates a tenant-local text key compatibility context.</summary>
    internal CompositeTextKeyContext(
        DbContextOptions<CompositeTextKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<CompositeTextKeyNode>();
        node.HasKey(entity => new
        {
            entity.TenantId,
            entity.NodeKey,
        });
        node
            .Property(entity => entity.NodeKey)
            .HasMaxLength(40)
            .ValueGeneratedNever();
        node
            .Property(entity => entity.ParentNodeKey)
            .HasMaxLength(40);
        node
            .Property(entity => entity.Name)
            .HasMaxLength(100);
        node
            .HasOne<CompositeTextKeyNode>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.TenantId,
                entity.ParentNodeKey,
            })
            .OnDelete(DeleteBehavior.Restrict);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.NodeKey)
            .HasScope(entity => entity.TenantId)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentNodeKey)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position)
            .OrderBy(entity => entity.Name));
    }
}
