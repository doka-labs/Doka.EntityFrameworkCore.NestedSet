namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies every bounded wave and the independent fully installed detached-snapshot controls.</summary>
/// <param name="Nodes">The complete validated import collection size.</param>
/// <param name="Offset">The first tracked node of the current bounded wave.</param>
/// <param name="CompleteSnapshot">Whether the complete native snapshot is already installed.</param>
public readonly record struct PreparedImportGuardBenchmarkCase(
    int Nodes,
    int Offset,
    bool CompleteSnapshot
)
{
    /// <inheritdoc />
    public override string ToString()
        => FormattableString.Invariant($"nodes-{Nodes}-offset-{Offset}-complete-{CompleteSnapshot}");
}
