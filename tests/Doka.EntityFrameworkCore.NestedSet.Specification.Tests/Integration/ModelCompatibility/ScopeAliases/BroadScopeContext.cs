namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps CLR-broad scope equality onto case-sensitive provider identities.</summary>
internal class BroadScopeContext : DbContext
{
    /// <summary>Creates a context for the model-compatibility database.</summary>
    internal BroadScopeContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var collation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsSqlServer()
                ? "Latin1_General_100_BIN2"
                : Database.IsNpgsql()
                    ? null
                    : "utf8mb4_bin";

        var node = modelBuilder.Entity<BroadScopeNode>();
        node.ToTable("BroadScopeNodes");
        node.HasKey(entity => new
        {
            entity.Scope,
            entity.Id,
        });

        var scope = node
            .Property(entity => entity.Scope)
            .HasConversion(value => value.Value, value => new BroadScope(value))
            .HasMaxLength(40);

        if (collation is not null)
        {
            scope.UseCollation(collation);
        }

        // WHY: EF's identity map must agree with the distinct provider values, despite the domain equality.
        var exact = new ValueComparer<BroadScope>(
            (first, second) => StringComparer.Ordinal.Equals(first!.Value, second!.Value),
            value => StringComparer.Ordinal.GetHashCode(value.Value),
            value => new BroadScope(value.Value));

        scope.Metadata.SetValueComparer(exact);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node
            .Property(entity => entity.Name)
            .HasMaxLength(40);

        node
            .HasOne<BroadScopeNode>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.Scope,
                entity.ParentId,
            })
            .OnDelete(DeleteBehavior.Restrict);

        node.HasNestedSet(nestedSet => nestedSet
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
