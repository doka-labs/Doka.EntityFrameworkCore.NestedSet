namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps the generated registry name that exists before a migration names it.</summary>
internal sealed class RegistryMigrationBeforeContext : DbContext
{
    /// <summary>Creates the pre-migration context.</summary>
    internal RegistryMigrationBeforeContext(
        DbContextOptions<RegistryMigrationBeforeContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => RegistryTableNode.Map<ReportingNode>(modelBuilder, "RegistryMigrationNodes", null);
}
