namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared BulkWriteBudgetTests contract on this project's database provider.</summary>
public sealed class BulkWriteBudgetTests : Specifications.BulkWriteBudgetTests,
    IClassFixture<ProviderFixture<BulkGeneratedFixture, SqliteEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="assigned">The fixture owned by this suite or its test collection.</param>
    /// <param name="generated">The fixture owned by this suite or its test collection.</param>
    public BulkWriteBudgetTests(
        ProviderFixture<RelationalFixture, SqliteEngine> assigned,
        ProviderFixture<BulkGeneratedFixture, SqliteEngine> generated
    ) : base(assigned, generated) { }
}
