namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs asynchronous composition and qualified concurrent snapshots on SQLite.</summary>
public sealed class HierarchyCompositionConsistencyTests : Specifications.HierarchyCompositionConsistencyTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The SQLite fixture owning the test database.</param>
    public HierarchyCompositionConsistencyTests(
        ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture
    ) : base(fixture) { }
}
