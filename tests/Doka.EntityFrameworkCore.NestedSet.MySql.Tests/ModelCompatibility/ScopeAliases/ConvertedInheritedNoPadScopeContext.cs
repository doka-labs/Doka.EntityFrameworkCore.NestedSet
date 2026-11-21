namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Combines inherited NO PAD Scope comparison with bounded scalar key transport.</summary>
/// <param name="options">The configured provider options for this exact context type.</param>
internal sealed class ConvertedInheritedNoPadScopeContext
    : InheritedNoPadScopeContext
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public ConvertedInheritedNoPadScopeContext(
        DbContextOptions<ConvertedInheritedNoPadScopeContext> options
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
