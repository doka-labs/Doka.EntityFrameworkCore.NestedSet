namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents a scoped hierarchy with a composite EF primary key and a scalar alternate NodeKey.</summary>
internal sealed class CompositeKeyNode
{
    /// <summary>Gets or sets the tenant component of both relational identities.</summary>
    internal int TenantId { get; set; }

    /// <summary>Gets or sets the row-local primary-key component.</summary>
    internal int RowId { get; set; }

    /// <summary>Gets or sets the stable scalar hierarchy identity.</summary>
    internal int NodeKey { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent NodeKey.</summary>
    internal int? ParentNodeKey { get; set; }

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

/// <summary>Maps the composite row identity independently of the hierarchy NodeKey.</summary>
internal sealed class CompositeKeyContext : DbContext
{
    /// <summary>Creates a composite-key compatibility context.</summary>
    internal CompositeKeyContext(
        DbContextOptions<CompositeKeyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<CompositeKeyNode>();
        node.HasKey(entity => new
        {
            entity.TenantId,
            entity.RowId,
        });
        node.HasAlternateKey(entity => new
        {
            entity.TenantId,
            entity.NodeKey,
        });
        node
            .Property(entity => entity.RowId)
            .ValueGeneratedNever();
        node
            .Property(entity => entity.NodeKey)
            .ValueGeneratedNever();
        node
            .Property(entity => entity.Name)
            .HasMaxLength(100);
        node
            .HasOne<CompositeKeyNode>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.TenantId,
                entity.ParentNodeKey,
            })
            .HasPrincipalKey(entity => new
            {
                entity.TenantId,
                entity.NodeKey,
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
