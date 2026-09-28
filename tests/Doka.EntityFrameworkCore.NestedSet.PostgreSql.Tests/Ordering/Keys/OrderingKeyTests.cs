namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared OrderingKeyTests contract on this project's database provider.</summary>
public sealed class OrderingKeyTests : Specifications.OrderingKeyTests,
    IClassFixture<ProviderFixture<OrderingKeyFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public OrderingKeyTests(
        ProviderFixture<OrderingKeyFixture, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
