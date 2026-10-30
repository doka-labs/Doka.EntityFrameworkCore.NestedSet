namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs indexed ancestor query contracts on Doka MySQL.</summary>
public sealed class AncestorIndexQueryTests : Specifications.AncestorIndexQueryTests
{
    /// <summary>Uses the collection-owned MySQL model database.</summary>
    /// <param name="fixture">The exact MySQL provider fixture.</param>
    public AncestorIndexQueryTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
