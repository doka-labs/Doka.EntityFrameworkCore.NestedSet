namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared OrderingRefreshScaleTests contract on this project's database provider.</summary>
public sealed class OrderingRefreshScaleTests : Specifications.OrderingRefreshScaleTests,
    IClassFixture<ProviderFixture<OrderingFixture, SqliteEngine>>,
    IClassFixture<ProviderFixture<OrderingKeyFixture, SqliteEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="relationalFixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="keyFixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="output">The test output sink.</param>
    public OrderingRefreshScaleTests(
        ProviderFixture<OrderingFixture, SqliteEngine> fixture,
        ProviderFixture<RelationalFixture, SqliteEngine> relationalFixture,
        ProviderFixture<OrderingKeyFixture, SqliteEngine> keyFixture,
        ITestOutputHelper output
    ) : base(fixture, relationalFixture, keyFixture, output) { }
}
