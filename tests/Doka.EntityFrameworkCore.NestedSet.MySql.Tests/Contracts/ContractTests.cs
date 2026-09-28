namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared ContractTests contract on this project's database provider.</summary>
public sealed class ContractTests : Specifications.ContractTests,
    IClassFixture<ProviderFixture<ContractFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public ContractTests(
        ProviderFixture<ContractFixture, MySqlEngine> fixture
    ) : base(fixture) { }
}
