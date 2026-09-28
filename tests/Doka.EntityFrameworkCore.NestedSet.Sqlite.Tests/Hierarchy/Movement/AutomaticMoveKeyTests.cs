namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared AutomaticMoveKeyTests contract on this project's database provider.</summary>
public sealed class AutomaticMoveKeyTests : Specifications.AutomaticMoveKeyTests,
    IClassFixture<ProviderFixture<OrderingKeyFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public AutomaticMoveKeyTests(
        ProviderFixture<OrderingKeyFixture, SqliteEngine> fixture
    ) : base(fixture) { }
}
