namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Uses typed Guid readers with the provider's native binary(16) hierarchy identities.</summary>
internal class GuidBinaryStorageContext : DbContext
{
    /// <summary>Uses the fixture's exact provider and database.</summary>
    /// <param name="options">The options for this native Guid model.</param>
    public GuidBinaryStorageContext(
        DbContextOptions<GuidBinaryStorageContext> options
    ) : base(options) { }

    /// <summary>Allows the converted model to retain its own typed options and model cache entry.</summary>
    /// <param name="options">The options belonging to the derived converted model.</param>
    protected GuidBinaryStorageContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<GuidBinaryStorageNode>();

        // WHY: The shared fixture retains tables for multiple context types in the same provider database.
        node.ToTable(GetType().Name);
        node.HasKey(entity => entity.Id);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node
            .Property(entity => entity.Name)
            .HasMaxLength(32);

        node.HasNestedSet(hierarchy => hierarchy
            .HasNodeKey(entity => entity.Id)
            .HasScope(entity => entity.Scope)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position)
            .OrderBy(entity => entity.Name));
    }
}
