namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Opts the existing operator-free converted Scope mapping into indexed ancestor composition.</summary>
/// <param name="options">The fixture-owned provider connection and observers.</param>
internal sealed class AncestorIndexScopeQueryContext
    : BroadScopeContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public AncestorIndexScopeQueryContext(
        DbContextOptions<AncestorIndexScopeQueryContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder
            .Entity<BroadScopeNode>()
            .ToTable("AncestorIndexScopeNodes")
            .HasNestedSet(mapping => mapping.HasAncestorIndex());
    }
}
