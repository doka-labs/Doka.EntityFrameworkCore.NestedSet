namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared ConfiguredMutableIdentityTests contract on this project's database provider.</summary>
public sealed class ConfiguredMutableIdentityTests : Specifications.ConfiguredMutableIdentityTests,
    IClassFixture<ProviderFixture<Specifications.ConfiguredMutableIdentityTests.ConfiguredIdentityFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public ConfiguredMutableIdentityTests(
        ProviderFixture<ConfiguredIdentityFixture, MySqlEngine> fixture
    ) : base(fixture) { }
}
