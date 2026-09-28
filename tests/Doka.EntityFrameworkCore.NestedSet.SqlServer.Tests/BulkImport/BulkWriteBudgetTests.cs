namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared BulkWriteBudgetTests contract on this project's database provider.</summary>
public sealed class BulkWriteBudgetTests : Specifications.BulkWriteBudgetTests,
    IClassFixture<ProviderFixture<BulkGeneratedFixture, SqlServerEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="assigned">The fixture owned by this suite or its test collection.</param>
    /// <param name="generated">The fixture owned by this suite or its test collection.</param>
    public BulkWriteBudgetTests(
        ProviderFixture<RelationalFixture, SqlServerEngine> assigned,
        ProviderFixture<BulkGeneratedFixture, SqlServerEngine> generated
    ) : base(assigned, generated) { }
}
