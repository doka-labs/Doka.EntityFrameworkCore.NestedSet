namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared FixtureTreeLifecycleTests contract on this project's database provider.</summary>
public sealed class FixtureTreeLifecycleTests : Specifications.FixtureTreeLifecycleTests,
    IClassFixture<ProviderFixture<ContractFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public FixtureTreeLifecycleTests(
        ProviderFixture<ContractFixture, SqlServerEngine> fixture
    ) : base(fixture) { }
}
