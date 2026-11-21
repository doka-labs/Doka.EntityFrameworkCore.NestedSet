namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Retains a bounded scalar aggregate rather than materializing hierarchy entities.</summary>
/// <param name="Anchor">The independently selected ancestor key.</param>
/// <param name="Contributions">The number of eligible payload nodes.</param>
/// <param name="KeySum">The sum of eligible keys, detecting substitution as well as dropped rows.</param>
internal sealed record CompositionBenchmarkTotal(
    int Anchor,
    long Contributions,
    long KeySum
);
