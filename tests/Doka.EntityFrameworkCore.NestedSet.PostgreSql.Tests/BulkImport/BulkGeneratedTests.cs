namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared BulkGeneratedTests contract on this project's database provider.</summary>
public sealed class BulkGeneratedTests : Specifications.BulkGeneratedTests,
    IClassFixture<ProviderFixture<BulkGeneratedFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public BulkGeneratedTests(
        ProviderFixture<BulkGeneratedFixture, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
