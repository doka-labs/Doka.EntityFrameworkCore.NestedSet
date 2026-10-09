namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs converted text principal equality against SQL Server's native collations.</summary>
public sealed class ConvertedTextCollationTests : Specifications.ConvertedTextCollationTests
{
    /// <summary>Uses the exact engine's existing compatibility fixture.</summary>
    /// <param name="fixture">The provider-owned database fixture.</param>
    public ConvertedTextCollationTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }
}
