namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Inherits binary comparison from a physical table without declaring a property or model collation.</summary>
/// <param name="options">The configured provider options for this exact context type.</param>
internal class InheritedBinaryScopeContext : BroadScopeContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public InheritedBinaryScopeContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        var node = modelBuilder.Entity<BroadScopeNode>();
        node.ToTable(
            GetType()
                .Name
            + "Nodes");

        node
            .Property(value => value.Scope)
            .UseCollation(null);

        // WHY: Scope inherits a configured table comparison even without property/model collations. Capturing
        // the hierarchy's table facet must preserve the same identity in payload membership and registry locks.
        node.HasAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
    }
}
