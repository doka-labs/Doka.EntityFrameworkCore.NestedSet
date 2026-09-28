namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared BulkStageGuardTests contract on this project's database provider.</summary>
public sealed class BulkStageGuardTests : Specifications.BulkStageGuardTests,
    IClassFixture<ProviderFixture<BulkStageGuardFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public BulkStageGuardTests(
        ProviderFixture<BulkStageGuardFixture, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
