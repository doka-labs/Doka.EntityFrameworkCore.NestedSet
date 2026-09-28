namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps structural values to backing fields whose public getters intentionally differ.</summary>
public sealed class FieldContext : DbContext
{
    /// <summary>Creates a configured-field-access context.</summary>
    /// <param name="options">The provider connection options.</param>
    public FieldContext(
        DbContextOptions<FieldContext> options
    ) : base(options) { }

    /// <summary>Configures field access so the library must follow EF's mapped accessors.</summary>
    /// <param name="modelBuilder">The builder for the field-access model.</param>
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var entity = modelBuilder.Entity<FieldNode>();
        entity.ToTable("ContractFieldNodes");
        entity
            .Property(x => x.Id)
            .ValueGeneratedNever();

        // Decorated CLR getters must never replace the raw field values that EF persists and queries.
        entity
            .Property(x => x.Left)
            .HasField("_left")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        entity
            .Property(x => x.Right)
            .HasField("_right")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        entity
            .Property(x => x.Depth)
            .HasField("_depth")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.HasNestedSet(x => x
            .HasTreeId(n => n.TreeId)
            .HasScope(n => n.Tree)
            .HasParent(n => n.ParentId));
    }
}
