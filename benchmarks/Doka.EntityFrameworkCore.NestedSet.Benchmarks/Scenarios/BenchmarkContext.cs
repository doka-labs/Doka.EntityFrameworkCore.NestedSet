namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Maps the same forest on every provider while retaining distinct manual and ordered EF models.</summary>
/// <remarks>
/// The library context base coordinates tracked hierarchy updates around ordinary SaveChangesAsync calls.
/// </remarks>
public abstract class BenchmarkContext : NestedSetDbContext
{
    /// <summary>Creates a context using an owned benchmark database.</summary>
    /// <param name="options">The provider and diagnostic interception settings.</param>
    protected BenchmarkContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the application hierarchy table.</summary>
    public DbSet<BenchmarkNode> Nodes => Set<BenchmarkNode>();

    /// <summary>Gets whether sibling order follows the mapped name.</summary>
    protected abstract bool Ordered { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<BenchmarkNode>();
        node.ToTable("BenchmarkNodes");
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node
            .Property(value => value.Name)
            .HasMaxLength(64);

        // WHY: NestedSet creates the scope-qualified parent FK; a second key-only relationship is ambiguous.
        node.HasNestedSet(mapping =>
        {
            mapping
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId);

            if (Ordered)
            {
                mapping.OrderBy(value => value.Name);
            }
        });

        modelBuilder
            .Entity<BenchmarkTrackedEntity>()
            .Property(value => value.Id)
            .ValueGeneratedNever();
    }
}
