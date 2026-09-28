namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared OrderingLockTests contract on this project's database provider.</summary>
public sealed class OrderingLockTests : Specifications.OrderingLockTests,
    IClassFixture<ProviderFixture<OrderingLockFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public OrderingLockTests(
        ProviderFixture<OrderingLockFixture, SqliteEngine> fixture
    ) : base(fixture) { }
}
