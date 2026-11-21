namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Distinguishes complete-plan, empty/populated navigation and typed scalar probes.</summary>
/// <param name="Operation">The actual guard entry point used by the workload.</param>
/// <param name="Nodes">The complete population, or number of independent populated parent pairs.</param>
public readonly record struct GuardProbeBenchmarkCase(
    string Operation,
    int Nodes
)
{
    /// <inheritdoc />
    public override string ToString() => FormattableString.Invariant($"{Operation}-{Nodes}");
}
