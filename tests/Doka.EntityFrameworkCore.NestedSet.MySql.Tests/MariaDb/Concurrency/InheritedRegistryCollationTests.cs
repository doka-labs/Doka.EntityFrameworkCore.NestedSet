namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the Doka family's inherited registry-collation regressions on MariaDB.</summary>
[Collection("Model compatibility")]
public sealed class InheritedRegistryCollationTests : MySql.InheritedRegistryCollationTestBase
{
    /// <summary>Uses the model database owned by this engine's test collection.</summary>
    /// <param name="fixture">The exact MariaDb resource owner.</param>
    public InheritedRegistryCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
