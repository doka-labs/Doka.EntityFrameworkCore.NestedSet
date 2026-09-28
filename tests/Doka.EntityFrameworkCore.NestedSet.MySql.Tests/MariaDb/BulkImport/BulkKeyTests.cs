namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared BulkKeyTests contract on this project's database provider.</summary>
public sealed class BulkKeyTests : Specifications.BulkKeyTests,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public BulkKeyTests(
        ProviderFixture<RelationalFixture, MariaDbEngine> fixture
    ) : base(fixture) { }
}
