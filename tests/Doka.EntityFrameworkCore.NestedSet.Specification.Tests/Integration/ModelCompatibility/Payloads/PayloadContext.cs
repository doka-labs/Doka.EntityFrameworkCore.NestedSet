namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps owned and complex payload beside independent nested-set entities.</summary>
internal sealed class PayloadContext : DbContext
{
    /// <summary>Creates a payload compatibility context.</summary>
    internal PayloadContext(
        DbContextOptions<PayloadContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var owned = modelBuilder.Entity<OwnedPayloadNode>();
        owned.ToTable("OwnedPayloadNodes");
        owned
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        owned.OwnsOne(entity => entity.Details, details => details.Property(value => value.Label));
        Configure(owned);

        var complex = modelBuilder.Entity<ComplexPayloadNode>();
        complex.ToTable("ComplexPayloadNodes");
        complex
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        complex.ComplexProperty(entity => entity.Details, details => details.Property(value => value.Label));
        Configure(complex);
    }

    /// <summary>Maps the structural roles of one payload-carrying node type.</summary>
    private static void Configure(
        EntityTypeBuilder<OwnedPayloadNode> node
    )
    {
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }

    /// <summary>Maps the structural roles of the complex-payload node.</summary>
    private static void Configure(
        EntityTypeBuilder<ComplexPayloadNode> node
    )
    {
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(entity => entity.TreeId)
            .HasParent(entity => entity.ParentId)
            .HasBounds(entity => entity.Left, entity => entity.Right)
            .HasDepth(entity => entity.Depth)
            .HasPosition(entity => entity.Position));
    }
}
