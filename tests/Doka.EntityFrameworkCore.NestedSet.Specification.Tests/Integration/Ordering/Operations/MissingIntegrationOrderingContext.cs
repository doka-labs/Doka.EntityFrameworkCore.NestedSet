namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Omits SaveChanges integration while mapping the same sorted hierarchy as the strict model.</summary>
public sealed class MissingIntegrationOrderingContext : DbContext
{
    /// <summary>Creates a negative-control context with the selected real provider.</summary>
    public MissingIntegrationOrderingContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => OrderingContext.ConfigureModel(modelBuilder, "Strict");
}
