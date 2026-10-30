namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared hierarchy-composition contract on SQLite.</summary>
public sealed class HierarchyCompositionTests : Specifications.HierarchyCompositionTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The SQLite fixture owning the test database.</param>
    public HierarchyCompositionTests(
        ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture
    ) : base(fixture) { }
}
