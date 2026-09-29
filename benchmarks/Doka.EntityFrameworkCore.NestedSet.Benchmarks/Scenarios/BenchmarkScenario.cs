namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies the live data and tracker population prepared before one operation.</summary>
/// <param name="Nodes">The total number of persisted nodes.</param>
/// <param name="Shape">The adjacency distribution within each tree.</param>
/// <param name="Trees">The number of independent coordinate spaces.</param>
/// <param name="Tracked">The number of unrelated unchanged tracked entities.</param>
public sealed record BenchmarkScenario(
    int Nodes,
    BenchmarkShape Shape,
    int Trees,
    int Tracked
)
{
    /// <summary>Checks that every tree has enough distinct anchors for the complete feature suite.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Trees);
        ArgumentOutOfRangeException.ThrowIfNegative(Tracked);
        ArgumentOutOfRangeException.ThrowIfLessThan(Nodes / Trees, 10);

        if (!Enum.IsDefined(Shape))
        {
            throw new ArgumentOutOfRangeException(nameof(Shape));
        }
    }
}
