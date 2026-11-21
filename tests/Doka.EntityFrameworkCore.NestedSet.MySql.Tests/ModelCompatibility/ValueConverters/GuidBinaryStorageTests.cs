namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs typed binary and converted varbinary Guid hierarchy contracts on MySQL.</summary>
[Collection("Model compatibility")]
public sealed class GuidBinaryStorageTests : GuidBinaryStorageTestBase
{
    /// <summary>Uses the existing MySQL model-compatibility database owner.</summary>
    /// <param name="fixture">The collection fixture bound to MySQL.</param>
    /// <param name="output">The output retaining actual rowset transport evidence.</param>
    public GuidBinaryStorageTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}
