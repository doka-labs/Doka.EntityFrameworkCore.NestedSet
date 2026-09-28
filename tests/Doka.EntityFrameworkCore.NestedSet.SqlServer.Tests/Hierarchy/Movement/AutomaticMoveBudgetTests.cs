namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared AutomaticMoveBudgetTests contract on this project's database provider.</summary>
public sealed class AutomaticMoveBudgetTests : Specifications.AutomaticMoveBudgetTests,
    IClassFixture<ProviderFixture<OrderingFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    public AutomaticMoveBudgetTests(
        ProviderFixture<OrderingFixture, SqlServerEngine> fixture
    ) : base(fixture) { }
}
