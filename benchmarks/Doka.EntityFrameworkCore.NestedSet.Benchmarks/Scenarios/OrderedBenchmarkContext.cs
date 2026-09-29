namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Uses database-managed name ordering and the normal coordinated SaveChanges path.</summary>
public sealed class OrderedBenchmarkContext : BenchmarkContext
{
    /// <summary>Creates the configured-order context.</summary>
    /// <param name="options">The owned database configuration.</param>
    public OrderedBenchmarkContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override bool Ordered => true;
}
