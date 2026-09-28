namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared AutomaticMoveKeyTests contract on this project's database provider.</summary>
public sealed class AutomaticMoveKeyTests : Specifications.AutomaticMoveKeyTests,
    IClassFixture<ProviderFixture<OrderingKeyFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public AutomaticMoveKeyTests(
        ProviderFixture<OrderingKeyFixture, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
