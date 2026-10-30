namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs asynchronous composition and qualified concurrent snapshots on SQL Server.</summary>
public sealed class HierarchyCompositionConsistencyTests : Specifications.HierarchyCompositionConsistencyTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The SQL Server fixture owning the test database.</param>
    public HierarchyCompositionConsistencyTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }
}
