namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Uses explicit sibling placements without automatic name sorting.</summary>
/// <remarks>A distinct context type gives EF its own model-cache identity without a custom cache service.</remarks>
public sealed class ManualBenchmarkContext : BenchmarkContext
{
    /// <summary>Creates the manual-order context.</summary>
    /// <param name="options">The owned database configuration.</param>
    public ManualBenchmarkContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override bool Ordered => false;
}
