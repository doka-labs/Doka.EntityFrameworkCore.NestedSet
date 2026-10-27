namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs overflow-safe Doka subtree contracts on MySql.</summary>
[Collection("Model compatibility")]
public sealed class MalformedSubtreeTests : MalformedSubtreeTestBase
{
    /// <summary>Uses the MySql-owned model database.</summary>
    /// <param name="fixture">The exact MySql provider fixture.</param>
    public MalformedSubtreeTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
