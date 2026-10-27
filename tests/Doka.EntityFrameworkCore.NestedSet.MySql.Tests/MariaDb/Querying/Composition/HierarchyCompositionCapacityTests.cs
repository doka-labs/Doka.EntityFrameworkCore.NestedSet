namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql.MariaDb;

/// <summary>Runs persisted composition capacity contracts on MariaDb.</summary>
[Collection("Allocation measurements")]
public sealed class HierarchyCompositionCapacityTests : Specifications.HierarchyCompositionCapacityTests,
    IClassFixture<ProviderFixture<RelationalFixture, MariaDbEngine>>
{
    /// <summary>Uses an isolated provider fixture and retains the measured evidence.</summary>
    /// <param name="fixture">The case-owned relational resource.</param>
    /// <param name="output">The sink retaining observed durations and allocations.</param>
    public HierarchyCompositionCapacityTests(
        ProviderFixture<RelationalFixture, MariaDbEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}
