namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs asynchronous composition and qualified concurrent snapshots on MariaDB.</summary>
public sealed class HierarchyCompositionConsistencyTests : Specifications.HierarchyCompositionConsistencyTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MariaDB fixture owning the test database.</param>
    public HierarchyCompositionConsistencyTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
