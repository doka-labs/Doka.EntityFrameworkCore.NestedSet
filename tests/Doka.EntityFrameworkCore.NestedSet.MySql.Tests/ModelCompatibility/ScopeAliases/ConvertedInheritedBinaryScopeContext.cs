namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Combines inherited binary Scope comparison with bounded scalar key transport.</summary>
/// <param name="options">The configured provider options for this exact context type.</param>
internal sealed class ConvertedInheritedBinaryScopeContext : InheritedBinaryScopeContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public ConvertedInheritedBinaryScopeContext(
        DbContextOptions<ConvertedInheritedBinaryScopeContext> options
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
