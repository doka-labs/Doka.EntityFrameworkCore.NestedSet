namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the Doka family's entity-splitting collation regressions on MySQL.</summary>
[Collection("Model compatibility")]
public sealed class EntitySplitCollationTests : EntitySplitCollationTestBase
{
    /// <summary>Uses the model database owned by this engine's test collection.</summary>
    /// <param name="fixture">The exact MySql resource owner.</param>
    public EntitySplitCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
