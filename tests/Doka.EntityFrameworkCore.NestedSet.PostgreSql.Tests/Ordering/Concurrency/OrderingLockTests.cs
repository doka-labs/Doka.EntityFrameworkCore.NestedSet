namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared OrderingLockTests contract on this project's database provider.</summary>
public sealed class OrderingLockTests : Specifications.OrderingLockTests,
    IClassFixture<ProviderFixture<OrderingLockFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public OrderingLockTests(
        ProviderFixture<OrderingLockFixture, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
