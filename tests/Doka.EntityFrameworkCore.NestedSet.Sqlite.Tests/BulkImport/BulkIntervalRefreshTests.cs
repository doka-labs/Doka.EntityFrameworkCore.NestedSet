namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared BulkIntervalRefreshTests contract on this project's database provider.</summary>
public sealed class BulkIntervalRefreshTests : Specifications.BulkIntervalRefreshTests,
    IClassFixture<ProviderFixture<BulkGeneratedFixture, SqliteEngine>>,
    IClassFixture<ProviderFixture<BulkIntervalRefreshFixture, SqliteEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="assigned">The fixture owned by this suite or its test collection.</param>
    /// <param name="generated">The fixture owned by this suite or its test collection.</param>
    /// <param name="ordered">The fixture owned by this suite or its test collection.</param>
    /// <param name="output">The test output sink.</param>
    public BulkIntervalRefreshTests(
        ProviderFixture<RelationalFixture, SqliteEngine> assigned,
        ProviderFixture<BulkIntervalRefreshFixture, SqliteEngine> generated,
        ProviderFixture<BulkGeneratedFixture, SqliteEngine> ordered,
        ITestOutputHelper output
    ) : base(assigned, generated, ordered, output) { }
}
