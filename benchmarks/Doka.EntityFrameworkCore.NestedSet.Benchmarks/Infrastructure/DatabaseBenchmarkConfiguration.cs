namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures exactly one database operation against the state restored for each iteration.</summary>
public sealed class DatabaseBenchmarkConfiguration : ManualConfig
{
    /// <summary>Creates the database default while permitting BenchmarkDotNet iteration and warmup overrides.</summary>
    public DatabaseBenchmarkConfiguration()
    {
        AddJob(
            Job
                .Default
                .WithRuntime(CoreRuntime.Core10_0)
                .WithCustomBuildConfiguration("Release")
                .WithStrategy(RunStrategy.Throughput)
                .RunOncePerIteration()
                .WithWarmupCount(3)
                .WithIterationCount(12)
                .AsDefault());
    }
}
