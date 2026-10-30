namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared actual signal sample contracts on MariaDb.</summary>
public sealed class SignalReportTests : Specifications.SignalReportTests
{
    /// <summary>Uses the provider's collection-owned model database.</summary>
    /// <param name="fixture">The MariaDb fixture owning the sample test database.</param>
    public SignalReportTests(
        ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine> fixture
    ) : base(fixture) { }
}
