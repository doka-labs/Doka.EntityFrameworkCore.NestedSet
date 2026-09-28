namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared FixtureTreeLifecycleTests contract on this project's database provider.</summary>
public sealed class FixtureTreeLifecycleTests : Specifications.FixtureTreeLifecycleTests,
    IClassFixture<ProviderFixture<ContractFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public FixtureTreeLifecycleTests(
        ProviderFixture<ContractFixture, MariaDbEngine> fixture
    ) : base(fixture) { }
}
