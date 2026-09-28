namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared OrderingRefreshScaleTests contract on this project's database provider.</summary>
public sealed class OrderingRefreshScaleTests : Specifications.OrderingRefreshScaleTests,
    IClassFixture<ProviderFixture<OrderingFixture, SqlServerEngine>>,
    IClassFixture<ProviderFixture<OrderingKeyFixture, SqlServerEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="relationalFixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="keyFixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="output">The test output sink.</param>
    public OrderingRefreshScaleTests(
        ProviderFixture<OrderingFixture, SqlServerEngine> fixture,
        ProviderFixture<RelationalFixture, SqlServerEngine> relationalFixture,
        ProviderFixture<OrderingKeyFixture, SqlServerEngine> keyFixture,
        ITestOutputHelper output
    ) : base(fixture, relationalFixture, keyFixture, output) { }
}
