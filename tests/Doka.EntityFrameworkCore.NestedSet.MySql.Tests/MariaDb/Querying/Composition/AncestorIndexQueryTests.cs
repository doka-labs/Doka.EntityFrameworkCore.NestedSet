namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs indexed ancestor query contracts on Doka MariaDB.</summary>
public sealed class AncestorIndexQueryTests : Specifications.AncestorIndexQueryTests
{
    /// <summary>Uses the collection-owned MariaDB model database.</summary>
    /// <param name="fixture">The exact MariaDB provider fixture.</param>
    public AncestorIndexQueryTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
