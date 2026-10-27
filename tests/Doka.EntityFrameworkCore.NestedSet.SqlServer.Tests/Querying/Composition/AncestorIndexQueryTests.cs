namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs indexed ancestor query contracts on native SQL Server.</summary>
public sealed class AncestorIndexQueryTests : Specifications.AncestorIndexQueryTests
{
    /// <summary>Uses the collection-owned SQL Server model database.</summary>
    /// <param name="fixture">The exact SQL Server provider fixture.</param>
    public AncestorIndexQueryTests(ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture) : base(fixture) { }
}
