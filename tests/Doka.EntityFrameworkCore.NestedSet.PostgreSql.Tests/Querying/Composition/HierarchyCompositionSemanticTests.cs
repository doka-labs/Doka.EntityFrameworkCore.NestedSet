namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared hierarchy semantic contracts on PostgreSql.</summary>
public sealed class HierarchyCompositionSemanticTests : Specifications.HierarchyCompositionSemanticTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The PostgreSql fixture owning the test database.</param>
    public HierarchyCompositionSemanticTests(
        ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
