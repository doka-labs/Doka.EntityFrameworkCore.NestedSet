namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared BulkInputTests contract on this project's database provider.</summary>
public sealed class BulkInputTests : Specifications.BulkInputTests,
    IClassFixture<ProviderFixture<RelationalFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public BulkInputTests(
        ProviderFixture<RelationalFixture, SqlServerEngine> fixture
    ) : base(fixture) { }
}
