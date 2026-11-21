namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Defines the actual small public insert and globally batched independent-root import workloads.</summary>
/// <param name="Forest">Whether to import 131 independent roots instead of inserting one child.</param>
/// <param name="Tracked">The unrelated unchanged application tracker population.</param>
public sealed record NativeTrackerWriteCase(bool Forest, int Tracked)
{
    /// <inheritdoc />
    public override string ToString() => $"{(Forest ? "forest-131-roots" : "single-child")}-tracked-{Tracked}";
}
