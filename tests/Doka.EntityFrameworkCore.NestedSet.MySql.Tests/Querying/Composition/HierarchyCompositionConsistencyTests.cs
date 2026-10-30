namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs asynchronous composition and qualified concurrent snapshots on MySQL.</summary>
public sealed class HierarchyCompositionConsistencyTests : Specifications.HierarchyCompositionConsistencyTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MySQL fixture owning the test database.</param>
    public HierarchyCompositionConsistencyTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
