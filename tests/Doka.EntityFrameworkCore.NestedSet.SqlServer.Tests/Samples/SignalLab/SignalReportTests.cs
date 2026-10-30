namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared actual signal sample contracts on SqlServer.</summary>
public sealed class SignalReportTests : Specifications.SignalReportTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The SqlServer fixture owning the sample test database.</param>
    public SignalReportTests(
        ProviderFixture<ModelCompatibilityDatabase, SqlServerEngine> fixture
    ) : base(fixture) { }
}
