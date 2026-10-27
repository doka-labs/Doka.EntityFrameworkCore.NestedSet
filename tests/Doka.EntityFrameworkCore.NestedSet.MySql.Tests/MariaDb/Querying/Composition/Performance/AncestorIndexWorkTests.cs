namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Qualifies native selected-ancestor work on the pinned MariaDB engine.</summary>
[Collection("Model compatibility")]
public sealed class AncestorIndexWorkTests : MySql.AncestorIndexWorkTestBase
{
    /// <summary>Uses the MariaDB-owned model database and native evidence sink.</summary>
    /// <param name="fixture">The exact MariaDB model fixture.</param>
    /// <param name="output">The sink retaining actual native tuple work.</param>
    public AncestorIndexWorkTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}
