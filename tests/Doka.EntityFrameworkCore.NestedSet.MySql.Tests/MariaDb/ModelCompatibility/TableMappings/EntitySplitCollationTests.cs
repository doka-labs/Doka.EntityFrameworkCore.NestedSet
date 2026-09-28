namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the Doka family's entity-splitting collation regressions on MariaDB.</summary>
[Collection("Model compatibility")]
public sealed class EntitySplitCollationTests : MySql.EntitySplitCollationTestBase
{
    /// <summary>Uses the model database owned by this engine's test collection.</summary>
    /// <param name="fixture">The exact MariaDb resource owner.</param>
    public EntitySplitCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
