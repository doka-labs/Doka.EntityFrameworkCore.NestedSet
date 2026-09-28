namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared SingleInsertExactValueTests contract on this project's database provider.</summary>
public sealed class SingleInsertExactValueTests : Specifications.SingleInsertExactValueTests,
    IClassFixture<ProviderFixture<BulkStageGuardFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public SingleInsertExactValueTests(
        ProviderFixture<BulkStageGuardFixture, SqliteEngine> fixture
    ) : base(fixture) { }
}
