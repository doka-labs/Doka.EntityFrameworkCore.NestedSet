namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared hierarchy-composition contract on the Doka MariaDB provider.</summary>
public sealed class HierarchyCompositionTests : Specifications.HierarchyCompositionTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MariaDB fixture owning the test database.</param>
    public HierarchyCompositionTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
