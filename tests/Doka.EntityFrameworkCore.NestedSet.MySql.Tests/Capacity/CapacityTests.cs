namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Runs real relational capacity contracts with isolated occupied-heap measurement.</summary>
[Collection("Allocation measurements")]
public sealed class CapacityTests : Specifications.CapacityTests,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    /// <summary>Uses the provider's isolated fixture and records the measured capacity evidence.</summary>
    /// <param name="fixture">The provider-owned relational database resource.</param>
    /// <param name="output">The sink retaining capacity measurements.</param>
    public CapacityTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}
