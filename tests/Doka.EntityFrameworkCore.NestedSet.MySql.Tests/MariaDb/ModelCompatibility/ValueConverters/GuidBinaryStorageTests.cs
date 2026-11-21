using Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs typed binary and converted varbinary Guid hierarchy contracts on MariaDB.</summary>
[Collection("Model compatibility")]
public sealed class GuidBinaryStorageTests : GuidBinaryStorageTestBase
{
    /// <summary>Uses the existing MariaDB model-compatibility database owner.</summary>
    /// <param name="fixture">The collection fixture bound to MariaDB.</param>
    /// <param name="output">The output retaining actual rowset transport evidence.</param>
    public GuidBinaryStorageTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}
