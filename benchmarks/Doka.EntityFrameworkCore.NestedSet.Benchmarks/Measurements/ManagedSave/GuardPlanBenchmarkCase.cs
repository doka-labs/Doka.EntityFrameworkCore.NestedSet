namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Retains independently selected tracked and authorized candidate counts.</summary>
/// <param name="Nodes">The complete tracked hierarchy population.</param>
/// <param name="Candidates">The exact modified entries authorized by the guard plan.</param>
public readonly record struct GuardPlanBenchmarkCase(
    int Nodes,
    int Candidates
)
{
    /// <inheritdoc />
    public override string ToString() => FormattableString.Invariant($"nodes-{Nodes}-candidates-{Candidates}");
}
