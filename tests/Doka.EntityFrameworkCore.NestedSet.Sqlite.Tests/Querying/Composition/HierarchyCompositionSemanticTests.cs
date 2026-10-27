namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared hierarchy semantic contracts on Sqlite.</summary>
public sealed class HierarchyCompositionSemanticTests : Specifications.HierarchyCompositionSemanticTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The Sqlite fixture owning the test database.</param>
    public HierarchyCompositionSemanticTests(
        ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture
    ) : base(fixture) { }
}
