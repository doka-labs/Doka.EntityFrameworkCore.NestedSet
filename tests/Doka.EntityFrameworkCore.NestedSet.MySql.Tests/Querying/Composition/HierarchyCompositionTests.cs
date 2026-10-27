namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared hierarchy-composition contract on the Doka MySQL provider.</summary>
public sealed class HierarchyCompositionTests : Specifications.HierarchyCompositionTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MySQL fixture owning the test database.</param>
    public HierarchyCompositionTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
