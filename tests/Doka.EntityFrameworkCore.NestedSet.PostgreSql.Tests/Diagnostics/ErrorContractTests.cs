namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared ErrorContractTests contract on this project's database provider.</summary>
public sealed class ErrorContractTests : Specifications.ErrorContractTests,
    IClassFixture<ProviderFixture<OrderingFixture, PostgreSqlEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="relational">The fixture owned by this suite or its test collection.</param>
    /// <param name="ordering">The fixture owned by this suite or its test collection.</param>
    public ErrorContractTests(
        ProviderFixture<RelationalFixture, PostgreSqlEngine> relational,
        ProviderFixture<OrderingFixture, PostgreSqlEngine> ordering
    ) : base(relational, ordering) { }
}
