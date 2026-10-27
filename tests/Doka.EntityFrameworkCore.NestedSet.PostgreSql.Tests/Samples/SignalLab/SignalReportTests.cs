namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared actual signal sample contracts on PostgreSql.</summary>
public sealed class SignalReportTests : Specifications.SignalReportTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The PostgreSql fixture owning the sample test database.</param>
    public SignalReportTests(
        ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
