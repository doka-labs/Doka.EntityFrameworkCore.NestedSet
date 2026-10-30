namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared composition model contracts on the Doka MariaDB provider.</summary>
public sealed class HierarchyCompositionModelTests : Specifications.HierarchyCompositionModelTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MariaDB fixture owning the test database.</param>
    public HierarchyCompositionModelTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
