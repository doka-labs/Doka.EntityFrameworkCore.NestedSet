namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies an actual former capacity construction expression and every variable bound into it.</summary>
/// <param name="Profile">Aggregate, Mixed, or a deep path/nearest control.</param>
/// <param name="Nodes">The original logical node or depth limit.</param>
/// <param name="Anchors">The selected source cardinality.</param>
/// <param name="Facts">The selected payload cardinality.</param>
public sealed record NativeCompositionCase(string Profile, int Nodes, int Anchors, int Facts)
{
    /// <inheritdoc />
    public override string ToString() => $"{Profile}-nodes-{Nodes}-anchors-{Anchors}-facts-{Facts}";
}
