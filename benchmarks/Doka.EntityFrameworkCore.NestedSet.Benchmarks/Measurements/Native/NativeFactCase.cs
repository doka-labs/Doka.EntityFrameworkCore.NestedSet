namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Identifies the actual indexed fact-selected ancestor expression and physical population.</summary>
/// <param name="Operation">Fact, Union, Any, Contains, or NotAny consumer composition.</param>
/// <param name="Nodes">The number of physical hierarchy nodes.</param>
/// <param name="Chain">Whether the physical tree is a deep chain rather than a star.</param>
/// <param name="Inclusive">Whether each source can also be its own ancestor.</param>
/// <param name="Matches">Whether the requested fact label exists.</param>
public sealed record NativeFactCase(string Operation, int Nodes, bool Chain, bool Inclusive, bool Matches)
{
    /// <summary>Gets the distinct selected source count from the original native workload.</summary>
    internal int Sources => Nodes == 1_000_000 ? 1_000 : 2;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Operation}-nodes-{Nodes}-chain-{Chain}-inclusive-{Inclusive}-matches-{Matches}";

    /// <summary>Gets every former captured-fact, union, large-selection and consumer-subquery measurement.</summary>
    public static IEnumerable<NativeFactCase> All
    {
        get
        {
            foreach (var inclusive in new[] { false, true })
            {
                foreach (var chain in new[] { false, true })
                {
                    foreach (var matches in new[] { false, true })
                    {
                        yield return new("Fact", 100_000, chain, inclusive, matches);
                    }
                }

                yield return new("Union", 100_000, false, inclusive, true);
                yield return new("Fact", 1_000_000, false, inclusive, true);
            }

            foreach (var operation in new[] { "Any", "Contains", "NotAny" })
            {
                yield return new(operation, 100_000, false, true, false);
                yield return new(operation, 100_000, false, true, true);
            }
        }
    }
}
