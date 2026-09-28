namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared NestedSetMutationFacadeBudgetTests contract on this project's database provider.</summary>
public sealed class NestedSetMutationFacadeBudgetTests : Specifications.NestedSetMutationFacadeBudgetTests,
    IClassFixture<ProviderFixture<RelationalFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public NestedSetMutationFacadeBudgetTests(
        ProviderFixture<RelationalFixture, SqlServerEngine> fixture
    ) : base(fixture) { }
}
