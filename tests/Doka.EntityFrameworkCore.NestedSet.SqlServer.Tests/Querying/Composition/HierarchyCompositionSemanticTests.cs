namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared hierarchy semantic contracts on SqlServer.</summary>
public sealed class HierarchyCompositionSemanticTests : Specifications.HierarchyCompositionSemanticTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The SqlServer fixture owning the test database.</param>
    public HierarchyCompositionSemanticTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }
}
