namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps one named and one default registry in the same model.</summary>
internal sealed class NamedRegistryContext : DbContext
{
    /// <summary>Creates the mixed-registry context.</summary>
    internal NamedRegistryContext(
        DbContextOptions<NamedRegistryContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        RegistryTableNode.Map<ReportingNode>(modelBuilder, "NamedRegistryReporting", "Reporting Hierarchy-Trees");
        RegistryTableNode.Map<OrganizationNode>(modelBuilder, "NamedRegistryOrganization", null);
    }
}
