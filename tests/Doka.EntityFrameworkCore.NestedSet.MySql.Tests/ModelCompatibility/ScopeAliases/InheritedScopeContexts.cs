namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Inherits binary comparison from a physical table without declaring a property or model collation.</summary>
/// <param name="options">The configured provider options for this exact context type.</param>
internal class InheritedBinaryScopeContext(DbContextOptions options) : BroadScopeContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        var node = modelBuilder.Entity<BroadScopeNode>();
        node.ToTable(GetType().Name + "Nodes");
        node
            .Property(value => value.Scope)
            .UseCollation(null);

        // WHY: Scope inherits a configured table comparison even without property/model collations. Capturing
        // the hierarchy's table facet must preserve the same identity in payload membership and registry locks.
        node.HasAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
    }
}

/// <summary>Combines inherited binary Scope comparison with bounded scalar key transport.</summary>
/// <param name="options">The configured provider options for this exact context type.</param>
internal sealed class ConvertedInheritedBinaryScopeContext(
    DbContextOptions<ConvertedInheritedBinaryScopeContext> options
) : InheritedBinaryScopeContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        ConvertedBroadScopeContext.ConfigureKeyConverter(modelBuilder.Entity<BroadScopeNode>());
    }
}

/// <summary>Uses an inherited MySQL NO PAD collation to distinguish trailing-space Scope identities.</summary>
/// <param name="options">The configured provider options for this exact context type.</param>
internal class InheritedNoPadScopeContext(DbContextOptions options) : InheritedBinaryScopeContext(options)
{
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

/// <summary>Combines inherited NO PAD Scope comparison with bounded scalar key transport.</summary>
/// <param name="options">The configured provider options for this exact context type.</param>
internal sealed class ConvertedInheritedNoPadScopeContext(DbContextOptions<ConvertedInheritedNoPadScopeContext> options)
    : InheritedNoPadScopeContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        ConvertedBroadScopeContext.ConfigureKeyConverter(modelBuilder.Entity<BroadScopeNode>());
    }
}

/// <summary>Leaves Scope comparison to the database default with no configured collation on any EF facet.</summary>
/// <param name="options">The provider options for this exact database-default model.</param>
internal class UnknownDatabaseScopeContext(DbContextOptions options) : BroadScopeContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        var node = modelBuilder.Entity<BroadScopeNode>();
        node.ToTable(GetType().Name + "Nodes");

        // WHY: The database owns this comparison. Removing every explicit EF facet distinguishes unknown native
        // semantics from the separately configured inherited-table cases without changing the database default.
        node
            .Property(value => value.Scope)
            .UseCollation(null);

        node.Metadata.RemoveAnnotation(RelationalAnnotationNames.Collation);
        modelBuilder.UseCollation(null);
    }
}

/// <summary>Exercises unknown database-default Scope comparison through bounded converted-key probes.</summary>
/// <param name="options">The provider options for this exact converted-key model.</param>
internal sealed class ConvertedUnknownDatabaseScopeContext(
    DbContextOptions<ConvertedUnknownDatabaseScopeContext> options
) : UnknownDatabaseScopeContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        ConvertedBroadScopeContext.ConfigureKeyConverter(modelBuilder.Entity<BroadScopeNode>());
    }
}
