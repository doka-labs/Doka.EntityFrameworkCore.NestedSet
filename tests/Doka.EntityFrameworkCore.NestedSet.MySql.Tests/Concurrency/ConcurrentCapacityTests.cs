namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs the shared concurrent capacity contract on MySQL.</summary>
[Collection(typeof(ConcurrentWriterTestDefinition))]
public sealed class ConcurrentCapacityTests : Specifications.ConcurrentCapacityTests,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated database fixture.</summary>
    /// <param name="fixture">The fixture owning this suite's database.</param>
    public ConcurrentCapacityTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture
    ) : base(fixture) { }
}
