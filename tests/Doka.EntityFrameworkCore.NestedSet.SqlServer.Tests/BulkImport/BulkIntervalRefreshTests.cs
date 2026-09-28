namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared BulkIntervalRefreshTests contract on this project's database provider.</summary>
public sealed class BulkIntervalRefreshTests : Specifications.BulkIntervalRefreshTests,
    IClassFixture<ProviderFixture<BulkGeneratedFixture, SqlServerEngine>>,
    IClassFixture<ProviderFixture<BulkIntervalRefreshFixture, SqlServerEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="assigned">The fixture owned by this suite or its test collection.</param>
    /// <param name="generated">The fixture owned by this suite or its test collection.</param>
    /// <param name="ordered">The fixture owned by this suite or its test collection.</param>
    /// <param name="output">The test output sink.</param>
    public BulkIntervalRefreshTests(
        ProviderFixture<RelationalFixture, SqlServerEngine> assigned,
        ProviderFixture<BulkIntervalRefreshFixture, SqlServerEngine> generated,
        ProviderFixture<BulkGeneratedFixture, SqlServerEngine> ordered,
        ITestOutputHelper output
    ) : base(assigned, generated, ordered, output) { }
}
