namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared BulkInsertTests contract on this project's database provider.</summary>
public sealed class BulkInsertTests : Specifications.BulkInsertTests,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public BulkInsertTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture) { }
}
