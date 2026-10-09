namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs the shared serialized writer contract on SQLite.</summary>
public sealed class ConcurrentWriterTests : Specifications.ConcurrentWriterTests,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses the provider assembly's isolated database fixture.</summary>
    /// <param name="fixture">The fixture owning this suite's database.</param>
    public ConcurrentWriterTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture
    ) : base(fixture) { }
}
