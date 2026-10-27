namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared actual signal sample contracts on Sqlite.</summary>
public sealed class SignalReportTests : Specifications.SignalReportTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The Sqlite fixture owning the sample test database.</param>
    public SignalReportTests(
        ProviderFixture<ModelCompatibilityDatabase, SqliteEngine> fixture
    ) : base(fixture) { }
}
