namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared OrderingTests contract on this project's database provider.</summary>
public sealed class OrderingTests : Specifications.OrderingTests,
    IClassFixture<ProviderFixture<OrderingFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public OrderingTests(
        ProviderFixture<OrderingFixture, SqliteEngine> fixture
    ) : base(fixture) { }
}
