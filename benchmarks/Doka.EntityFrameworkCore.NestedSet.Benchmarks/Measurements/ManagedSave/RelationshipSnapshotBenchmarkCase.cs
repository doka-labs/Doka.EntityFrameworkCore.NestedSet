namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Preserves the exact capture, skipped-write, endpoint and native-membership workload variants.</summary>
/// <param name="Operation">The actual production operation selected for measurement.</param>
/// <param name="Nodes">The tracked entries or owner width.</param>
/// <param name="AllEntries">Whether complete capture includes the entire update set.</param>
/// <param name="WarmNativeMaps">Whether this fixture initializes EF's native map before complete capture.</param>
/// <param name="Hierarchy">Whether skipped writes are Added hierarchy nodes instead of ordinary owners.</param>
public readonly record struct RelationshipSnapshotBenchmarkCase(
    string Operation,
    int Nodes,
    bool AllEntries,
    bool WarmNativeMaps,
    bool Hierarchy
)
{
    /// <inheritdoc />
    public override string ToString()
        => FormattableString.Invariant(
            $"{Operation}-{Nodes}-all-{AllEntries}-warm-{WarmNativeMaps}-hierarchy-{Hierarchy}");
}
