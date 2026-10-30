namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared hierarchy semantic contracts on MySql.</summary>
public sealed class HierarchyCompositionSemanticTests : Specifications.HierarchyCompositionSemanticTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MySql fixture owning the test database.</param>
    public HierarchyCompositionSemanticTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
