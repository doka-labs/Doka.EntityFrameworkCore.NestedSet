namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared MutationDispatchContractTests contract on this project's database provider.</summary>
public sealed class MutationDispatchContractTests : Specifications.MutationDispatchContractTests,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public MutationDispatchContractTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture) { }
}
