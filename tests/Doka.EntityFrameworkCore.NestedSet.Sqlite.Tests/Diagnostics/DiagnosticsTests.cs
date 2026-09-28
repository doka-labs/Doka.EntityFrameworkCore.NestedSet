namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared DiagnosticsTests contract on this project's database provider.</summary>
public sealed class DiagnosticsTests : Specifications.DiagnosticsTests,
    IClassFixture<ProviderFixture<OrderingFixture, SqliteEngine>>,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated fixtures and test output.</summary>
    /// <param name="fixture">The fixture owned by this suite or its test collection.</param>
    /// <param name="relational">The fixture owned by this suite or its test collection.</param>
    public DiagnosticsTests(
        ProviderFixture<OrderingFixture, SqliteEngine> fixture,
        ProviderFixture<RelationalFixture, SqliteEngine> relational
    ) : base(fixture, relational) { }
}
