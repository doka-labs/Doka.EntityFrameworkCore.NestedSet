namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Stores each offset explicitly while EF tracks the complete scope representation.</summary>
internal sealed class OffsetScopeContext : DbContext
{
    /// <summary>Creates a context for the model-compatibility database.</summary>
    internal OffsetScopeContext(
        DbContextOptions<OffsetScopeContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<OffsetScopeNode>();
        node.ToTable("OffsetScopeNodes");
        node.HasKey(entity => new
        {
            entity.Scope,
            entity.Id,
        });

        var scope = node
            .Property(entity => entity.Scope)
            .HasConversion(
                value => value.ToString("O", CultureInfo.InvariantCulture),
                value => DateTimeOffset.ParseExact(
                    value,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind))
            .HasMaxLength(40);

        // WHY: EF must distinguish the stored offsets even though DateTimeOffset.Equals compare instants.
        var exact = new ValueComparer<DateTimeOffset>(
            (first, second) => first.Ticks == second.Ticks && first.Offset == second.Offset,
            value => value.Ticks.GetHashCode() ^ value.Offset.GetHashCode(),
            value => value);

        scope.Metadata.SetValueComparer(exact);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node
            .Property(entity => entity.Name)
            .HasMaxLength(40);

        node
            .HasOne<OffsetScopeNode>()
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
