namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies the original complete/native capture and exceptional recovery graph shapes.</summary>
/// <param name="Operation">The actual snapshot operation selected for the graph.</param>
/// <param name="Rows">The number of complex elements, each with two nested leaves.</param>
public readonly record struct ComplexGraphBenchmarkCase(
    string Operation,
    int Rows
)
{
    /// <inheritdoc />
    public override string ToString() => FormattableString.Invariant($"{Operation}-{Rows}");
}
