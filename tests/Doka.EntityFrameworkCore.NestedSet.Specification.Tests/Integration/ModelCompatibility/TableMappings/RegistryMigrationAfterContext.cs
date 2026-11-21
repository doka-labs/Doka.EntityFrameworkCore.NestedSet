namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps the same hierarchy after the migration named its registry.</summary>
internal sealed class RegistryMigrationAfterContext : DbContext
{
    /// <summary>Creates the post-migration context.</summary>
    internal RegistryMigrationAfterContext(
        DbContextOptions<RegistryMigrationAfterContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => RegistryTableNode.Map<ReportingNode>(modelBuilder, "RegistryMigrationNodes", "Reporting_MigratedTrees");
}
