namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Qualifies native selected-ancestor work on the pinned MySQL engine.</summary>
[Collection("Model compatibility")]
public sealed class AncestorIndexWorkTests : AncestorIndexWorkTestBase
{
    /// <summary>Uses the MySQL-owned model database and native evidence sink.</summary>
    /// <param name="fixture">The exact MySQL model fixture.</param>
    /// <param name="output">The sink retaining actual native tuple work.</param>
    public AncestorIndexWorkTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}
