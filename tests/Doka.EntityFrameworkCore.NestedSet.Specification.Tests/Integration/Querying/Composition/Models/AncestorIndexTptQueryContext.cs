namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Keeps the alternate TPT model independent from the TPH mapping of the same CLR base type.</summary>
/// <param name="options">The fixture-owned provider connection.</param>
internal sealed class AncestorIndexTptQueryContext(DbContextOptions<AncestorIndexTptQueryContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => AncestorIndexMappedContext.ConfigureInheritance(modelBuilder, tpt: true);
}
