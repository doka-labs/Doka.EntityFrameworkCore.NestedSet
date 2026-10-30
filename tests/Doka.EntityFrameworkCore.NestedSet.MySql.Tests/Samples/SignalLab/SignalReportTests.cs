namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared actual signal sample contracts on MySql.</summary>
public sealed class SignalReportTests : Specifications.SignalReportTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MySql fixture owning the sample test database.</param>
    public SignalReportTests(
        ProviderFixture<ModelCompatibilityDatabase, MySqlEngine> fixture
    ) : base(fixture) { }
}
