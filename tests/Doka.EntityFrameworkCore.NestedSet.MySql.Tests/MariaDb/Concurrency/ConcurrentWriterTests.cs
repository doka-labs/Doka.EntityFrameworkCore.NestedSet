namespace Doka.EntityFrameworkCore.NestedSet.Tests.MariaDb;

/// <summary>Runs the shared serialized writer contract on MariaDB.</summary>
[Collection(typeof(ConcurrentWriterTestDefinition))]
public sealed class ConcurrentWriterTests : Specifications.ConcurrentWriterTests,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses the provider assembly's isolated database fixture.</summary>
    /// <param name="fixture">The fixture owning this suite's database.</param>
    public ConcurrentWriterTests(
        ProviderFixture<RelationalFixture, MariaDbEngine> fixture
    ) : base(fixture) { }
}
