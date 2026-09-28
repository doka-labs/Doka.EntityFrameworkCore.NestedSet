namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared OrderingKeyTests contract on this project's database provider.</summary>
public sealed class OrderingKeyTests : Specifications.OrderingKeyTests,
    IClassFixture<ProviderFixture<OrderingKeyFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public OrderingKeyTests(
        ProviderFixture<OrderingKeyFixture, MariaDbEngine> fixture
    ) : base(fixture) { }
}
