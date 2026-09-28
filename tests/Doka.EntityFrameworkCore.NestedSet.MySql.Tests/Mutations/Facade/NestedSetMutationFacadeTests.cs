namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared NestedSetMutationFacadeTests contract on this project's database provider.</summary>
public sealed class NestedSetMutationFacadeTests : Specifications.NestedSetMutationFacadeTests,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public NestedSetMutationFacadeTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture
    ) : base(fixture) { }
}
