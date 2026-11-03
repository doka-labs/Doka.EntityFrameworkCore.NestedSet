namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Isolates the prepared-owner model from the unchanged generic benchmark schema.</summary>
internal sealed class PreparedOwnerContext : NestedSetDbContext
{
    /// <summary>Uses the same launcher-owned provider and connection as the other database benchmarks.</summary>
    internal PreparedOwnerContext(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets the synthetic hierarchy whose scalar scope refers to an external owner.</summary>
    internal DbSet<PreparedOwnerNode> Nodes => Set<PreparedOwnerNode>();

    /// <summary>Gets persisted ordinary owners and their inverse hierarchy collection.</summary>
    internal DbSet<PreparedOwner> Owners => Set<PreparedOwner>();

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var owner = modelBuilder.Entity<PreparedOwner>();
        owner
            .Property(value => value.Id)
            .ValueGeneratedNever();

        owner
            .Property(value => value.Payload)
            .HasMaxLength(64);
        var node = modelBuilder.Entity<PreparedOwnerNode>();

        // WHY: Reuse the shared untimed observer's existing hierarchy-table classification.
        node.ToTable("BenchmarkNodes");
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();

        node
            .Property(value => value.Name)
            .HasMaxLength(64);

        node.HasNestedSet(mapping => mapping
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));

        // WHY: Inputs retain only the scalar FK; a populated backreference would violate detached import input.
        node
            .HasOne<PreparedOwner>()
            .WithMany(value => value.Nodes)
            .HasForeignKey(value => value.Scope)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
