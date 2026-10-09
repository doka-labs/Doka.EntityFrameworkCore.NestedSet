namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Runs the shared serialized writer contract on SQL Server.</summary>
[Collection(typeof(ConcurrentWriterTestDefinition))]
public sealed class ConcurrentWriterTests : Specifications.ConcurrentWriterTests,
    IClassFixture<ProviderFixture<RelationalFixture, SqlServerEngine>>
{
    /// <summary>Uses the provider assembly's isolated database fixture.</summary>
    /// <param name="fixture">The fixture owning this suite's database.</param>
    public ConcurrentWriterTests(
        ProviderFixture<RelationalFixture, SqlServerEngine> fixture
    ) : base(fixture) { }
}
