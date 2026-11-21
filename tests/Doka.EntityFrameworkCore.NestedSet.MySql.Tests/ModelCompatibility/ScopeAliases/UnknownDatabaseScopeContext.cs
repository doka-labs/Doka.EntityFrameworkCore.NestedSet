namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Leaves Scope comparison to the database default with no configured collation on any EF facet.</summary>
/// <param name="options">The provider options for this exact database-default model.</param>
internal class UnknownDatabaseScopeContext : BroadScopeContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public UnknownDatabaseScopeContext(
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

        // WHY: The database owns this comparison. Removing every explicit EF facet distinguishes unknown native
        // semantics from the separately configured inherited-table cases without changing the database default.
        node
            .Property(value => value.Scope)
            .UseCollation(null);

        node.Metadata.RemoveAnnotation(RelationalAnnotationNames.Collation);
        modelBuilder.UseCollation(null);
    }
}
