namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared DiagnosticsTests contract on this project's database provider.</summary>
public sealed class DiagnosticsTests : Specifications.DiagnosticsTests,
    IClassFixture<ProviderFixture<OrderingFixture, MariaDbEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="relational">The fixture owned by this suite or its test collection.</param>
    public DiagnosticsTests(
        ProviderFixture<OrderingFixture, MariaDbEngine> fixture,
        ProviderFixture<RelationalFixture, MariaDbEngine> relational
    ) : base(fixture, relational) { }
}
