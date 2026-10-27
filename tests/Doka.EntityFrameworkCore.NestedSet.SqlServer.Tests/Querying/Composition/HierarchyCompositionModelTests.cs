namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared composition model contracts on native SQL Server.</summary>
public sealed class HierarchyCompositionModelTests : Specifications.HierarchyCompositionModelTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The SQL Server fixture owning the test database.</param>
    public HierarchyCompositionModelTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }
}
