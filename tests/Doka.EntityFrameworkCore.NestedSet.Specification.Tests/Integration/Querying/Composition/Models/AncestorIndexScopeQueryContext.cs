namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Opts the existing operator-free converted Scope mapping into indexed ancestor composition.</summary>
/// <param name="options">The fixture-owned provider connection and observers.</param>
internal sealed class AncestorIndexScopeQueryContext(DbContextOptions<AncestorIndexScopeQueryContext> options)
    : BroadScopeContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BroadScopeNode>()
            .ToTable("AncestorIndexScopeNodes")
            .HasNestedSet(mapping => mapping.HasAncestorIndex());
    }
}
