namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared RegistryMigrationProviderTests contract on this project's database provider.</summary>
public sealed class RegistryMigrationProviderTests : Specifications.RegistryMigrationProviderTests,
    IClassFixture<ProviderFixture<OrderingFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public RegistryMigrationProviderTests(
        ProviderFixture<OrderingFixture, MySqlEngine> fixture
    ) : base(fixture) { }
}
