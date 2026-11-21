namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies native write measurement lifecycles without numerical allocation or timing assertions.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class NativeWriteMeasurementBenchmarkTests
{
    /// <summary>Actual child and forest work preserves its save waves and complete input geometry.</summary>
    /// <param name="forest">Whether the public operation imports independent roots.</param>
    /// <param name="tracked">The unchanged application tracker population.</param>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 20)]
    [InlineData(true, 0)]
    [InlineData(true, 20)]
    public async Task Write_TrackerProfiles_PersistCompleteWork(bool forest, int tracked)
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS");
        new BenchmarkRunOptions { Engine = BenchmarkEngine.SqliteMemory }.ApplyEnvironment();
        var benchmark = new NativeTrackerWriteBenchmarks { Case = new(forest, tracked) };

        try
        {
            await benchmark.PrepareAsync(TestContext.Current.CancellationToken);

            // Act
            await benchmark.Write();

            // Assert
            await benchmark.VerifyAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await benchmark.CleanupAsync();
            Environment.SetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS", previous);
        }
    }

    /// <summary>An unexecuted clean boundary cannot pass its correctness oracle.</summary>
    [Fact]
    public async Task VerifyAsync_UnexecutedCleanBoundary_RejectsNoOp()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS");
        new BenchmarkRunOptions { Engine = BenchmarkEngine.SqliteMemory }.ApplyEnvironment();
        var benchmark = new CleanMutationBoundaryBenchmarks { Tracked = 0 };

        try
        {
            await benchmark.PrepareAsync(TestContext.Current.CancellationToken);

            // Act
            var error = await Record.ExceptionAsync(() => benchmark.VerifyAsync(TestContext.Current.CancellationToken));

            // Assert
            Assert.IsType<InvalidOperationException>(error);
        }
        finally
        {
            await benchmark.CleanupAsync();
            Environment.SetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS", previous);
        }
    }

    /// <summary>Dense, sparse and adjacent saves retain complete tracked coordinates and caller state acceptance.</summary>
    /// <param name="sparse">Whether only the moved child is tracked.</param>
    /// <param name="adjacent">Whether the moved child crosses only its immediate sibling.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SaveAndRefresh_ActualOrderedSave_RetainsTrackedCoordinates(bool sparse, bool adjacent)
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS");
        new BenchmarkRunOptions { Engine = BenchmarkEngine.SqliteMemory }.ApplyEnvironment();
        var benchmark = new NativeRefreshBenchmarks { Case = new(10, sparse, adjacent) };

        try
        {
            benchmark.Initialize();
            await benchmark.PrepareAsync(TestContext.Current.CancellationToken);

            // Act
            await benchmark.SaveAndRefresh();

            // Assert
            await benchmark.VerifyAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await benchmark.CleanupAsync();
            Environment.SetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS", previous);
        }
    }

    /// <summary>Every medium, mixed and deep construction profile remains an unopened deferred query.</summary>
    /// <param name="engine">The actual provider metadata dialect.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    public async Task Construct_CapacityProfiles_PreservesAllDeferredVariants(BenchmarkEngine engine)
    {
        // Arrange
        var benchmark = new NativeCompositionConstructionBenchmarks { Engine = engine };
        benchmark.Initialize();

        try
        {
            // Act
            var queries = NativeCompositionConstructionBenchmarks.Cases.Select(value =>
            {
                benchmark.Case = value;

                return benchmark.Construct();
            }).ToArray();

            // Assert
            Assert.Equal(94, queries.Length);
            Assert.All(queries, query => Assert.IsAssignableFrom<IQueryable>(query));
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }
}
