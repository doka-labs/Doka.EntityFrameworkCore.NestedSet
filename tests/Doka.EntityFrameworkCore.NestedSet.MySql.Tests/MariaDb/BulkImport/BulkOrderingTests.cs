namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared BulkOrderingTests contract on this project's database provider.</summary>
public sealed class BulkOrderingTests : Specifications.BulkOrderingTests,
    IClassFixture<ProviderFixture<OrderingFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public BulkOrderingTests(
        ProviderFixture<OrderingFixture, MariaDbEngine> fixture
    ) : base(fixture) { }
}
