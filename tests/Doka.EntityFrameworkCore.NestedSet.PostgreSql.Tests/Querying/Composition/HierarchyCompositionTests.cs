namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared hierarchy-composition contract on PostgreSQL.</summary>
public sealed class HierarchyCompositionTests : Specifications.HierarchyCompositionTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The PostgreSQL fixture owning the test database.</param>
    public HierarchyCompositionTests(
        ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
