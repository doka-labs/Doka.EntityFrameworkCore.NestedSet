namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Exercises unknown database-default Scope comparison through bounded converted-key probes.</summary>
/// <param name="options">The provider options for this exact converted-key model.</param>
internal sealed class ConvertedUnknownDatabaseScopeContext : UnknownDatabaseScopeContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public ConvertedUnknownDatabaseScopeContext(
        DbContextOptions<ConvertedUnknownDatabaseScopeContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        ConvertedBroadScopeContext.ConfigureKeyConverter(modelBuilder.Entity<BroadScopeNode>());
    }
}
