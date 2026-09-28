namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared TypedParentContractTests contract on this project's database provider.</summary>
public sealed class TypedParentContractTests : Specifications.TypedParentContractTests,
    IClassFixture<ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="convertedFixture">The fixture owned by this suite or its test collection.</param>
    public TypedParentContractTests(
        ProviderFixture<RelationalFixture, MariaDbEngine> fixture,
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> convertedFixture
    ) : base(fixture, convertedFixture) { }
}
