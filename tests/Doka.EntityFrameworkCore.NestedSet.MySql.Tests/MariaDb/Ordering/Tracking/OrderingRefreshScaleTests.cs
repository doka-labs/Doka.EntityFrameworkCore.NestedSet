namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared OrderingRefreshScaleTests contract on this project's database provider.</summary>
public sealed class OrderingRefreshScaleTests : Specifications.OrderingRefreshScaleTests,
    IClassFixture<ProviderFixture<OrderingFixture, MariaDbEngine>>,
    IClassFixture<ProviderFixture<OrderingKeyFixture, MariaDbEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="relationalFixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="keyFixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="output">The test output sink.</param>
    public OrderingRefreshScaleTests(
        ProviderFixture<OrderingFixture, MariaDbEngine> fixture,
        ProviderFixture<RelationalFixture, MariaDbEngine> relationalFixture,
        ProviderFixture<OrderingKeyFixture, MariaDbEngine> keyFixture,
        ITestOutputHelper output
    ) : base(fixture, relationalFixture, keyFixture, output) { }
}
