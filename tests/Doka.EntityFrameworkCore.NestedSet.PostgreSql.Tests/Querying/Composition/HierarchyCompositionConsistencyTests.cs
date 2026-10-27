namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs asynchronous composition and qualified concurrent snapshots on PostgreSQL.</summary>
public sealed class HierarchyCompositionConsistencyTests : Specifications.HierarchyCompositionConsistencyTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The PostgreSQL fixture owning the test database.</param>
    public HierarchyCompositionConsistencyTests(
        ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
