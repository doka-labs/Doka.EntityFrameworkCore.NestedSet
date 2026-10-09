namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Runs the shared concurrent capacity contract on PostgreSQL.</summary>
[Collection(typeof(ConcurrentWriterTestDefinition))]
public sealed class ConcurrentCapacityTests : Specifications.ConcurrentCapacityTests,
    IClassFixture<ProviderFixture<RelationalFixture, PostgreSqlEngine>>
{
    /// <summary>Uses the provider assembly's isolated database fixture.</summary>
    /// <param name="fixture">The fixture owning this suite's database.</param>
    public ConcurrentCapacityTests(
        ProviderFixture<RelationalFixture, PostgreSqlEngine> fixture
    ) : base(fixture) { }
}
