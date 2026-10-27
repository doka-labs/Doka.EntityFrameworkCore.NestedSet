namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared hierarchy-composition contract on SQL Server.</summary>
public sealed class HierarchyCompositionTests : Specifications.HierarchyCompositionTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The SQL Server fixture owning the test database.</param>
    public HierarchyCompositionTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }
}
