namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared OrderingQueryShapeTests contract on this project's database provider.</summary>
public sealed class OrderingQueryShapeTests : Specifications.OrderingQueryShapeTests,
    IClassFixture<ProviderFixture<OrderingKeyFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="output">The test output sink.</param>
    public OrderingQueryShapeTests(
        ProviderFixture<OrderingKeyFixture, SqliteEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}
