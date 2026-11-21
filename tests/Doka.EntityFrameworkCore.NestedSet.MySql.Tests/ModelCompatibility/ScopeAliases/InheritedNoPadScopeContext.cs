namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Uses an inherited MySQL NO PAD collation to distinguish trailing-space Scope identities.</summary>
/// <param name="options">The configured provider options for this exact context type.</param>
internal class InheritedNoPadScopeContext : InheritedBinaryScopeContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public InheritedNoPadScopeContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder
            .Entity<BroadScopeNode>()
            .HasAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_0900_bin");
    }
}
