namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared composition model contracts on SQLite.</summary>
public sealed class HierarchyCompositionModelTests : Specifications.HierarchyCompositionModelTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The SQLite fixture owning the test database.</param>
    public HierarchyCompositionModelTests(
        ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture
    ) : base(fixture) { }
}
