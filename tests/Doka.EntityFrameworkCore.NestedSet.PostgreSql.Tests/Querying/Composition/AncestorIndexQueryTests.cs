namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs indexed ancestor query contracts on PostgreSQL.</summary>
public sealed class AncestorIndexQueryTests : Specifications.AncestorIndexQueryTests
{
    /// <summary>Uses the collection-owned PostgreSQL model database.</summary>
    /// <param name="fixture">The exact PostgreSQL provider fixture.</param>
    public AncestorIndexQueryTests(
        ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
