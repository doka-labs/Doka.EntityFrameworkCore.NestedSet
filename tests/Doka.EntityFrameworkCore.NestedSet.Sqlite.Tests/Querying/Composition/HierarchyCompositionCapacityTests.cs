namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Runs persisted composition capacity contracts on Sqlite.</summary>
[Collection("Allocation measurements")]
public sealed class HierarchyCompositionCapacityTests : Specifications.HierarchyCompositionCapacityTests,
    IClassFixture<ProviderFixture<RelationalFixture, SqliteEngine>>
{
    /// <summary>Uses an isolated provider fixture and retains the measured evidence.</summary>
    /// <param name="fixture">The case-owned relational resource.</param>
    /// <param name="output">The sink retaining observed durations and allocations.</param>
    public HierarchyCompositionCapacityTests(
        ProviderFixture<RelationalFixture, SqliteEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture, output) { }
}
