namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared OrderingTrackerTests contract on this project's database provider.</summary>
public sealed class OrderingTrackerTests : Specifications.OrderingTrackerTests,
    IClassFixture<ProviderFixture<SaveSemanticsFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public OrderingTrackerTests(
        ProviderFixture<SaveSemanticsFixture, SqlServerEngine> fixture
    ) : base(fixture) { }
}
