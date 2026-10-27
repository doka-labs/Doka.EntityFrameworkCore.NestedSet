namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared composition model contracts on PostgreSQL.</summary>
public sealed class HierarchyCompositionModelTests : Specifications.HierarchyCompositionModelTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The PostgreSQL fixture owning the test database.</param>
    public HierarchyCompositionModelTests(
        ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
