namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared composition model contracts on the Doka MySQL provider.</summary>
public sealed class HierarchyCompositionModelTests : Specifications.HierarchyCompositionModelTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MySQL fixture owning the test database.</param>
    public HierarchyCompositionModelTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
