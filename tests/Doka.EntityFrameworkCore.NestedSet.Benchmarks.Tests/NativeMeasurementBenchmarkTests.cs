namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Checks real native measurement workloads and their independent correctness boundaries.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class NativeMeasurementBenchmarkTests
{
    /// <summary>The smallest genuine tracker baseline remains discoverable without inventing ten-node forests.</summary>
    [Fact]
    public void Discover_NativeGuardCases_PreservesActualSmallBaselines()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS");
        var options = new BenchmarkRunOptions { Engine = BenchmarkEngine.SqliteMemory };
        options.ApplyEnvironment();
        var configuration = ManualConfig.Create(BenchmarkConfiguration.Create(options))
            .AddFilter(new GlobFilter(["*NativeGuardBenchmarks.ProtectTrackedIdentities"]));

        try
        {
            // Act
            using var selected = BenchmarkSelection.Discover(configuration, true);
            var scenarios = selected.DatabaseCases.Select(BenchmarkSelection.CreateScenario).ToArray();

            // Assert
            Assert.Equal(15, selected.DatabaseCases.Count);
            Assert.Contains(scenarios, scenario => scenario.Nodes == 3 && scenario.Tracked == 1);
            Assert.Contains(scenarios, scenario => scenario.Tracked == 769 && scenario.Trees == 66);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS", previous);
        }
    }

    /// <summary>Invalid guard geometry fails before opening a database or creating a resource.</summary>
    /// <param name="candidates">The supplied tracked population.</param>
    /// <param name="requests">The supplied reservation count.</param>
    /// <param name="delete">Whether the operation deletes a single tree.</param>
    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 2, true)]
    public void Initialize_NativeGuardInvalidCase_RejectsBeforeSetup(int candidates, int requests, bool delete)
    {
        // Arrange
        var benchmark = new NativeGuardBenchmarks { Case = new(NativeGuardKeyKind.NativeNumber, candidates, requests, delete) };

        // Act
        var error = Record.Exception(benchmark.Initialize);

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(error);
    }

    /// <summary>All four key shapes perform real guarded work and reject an unexecuted deletion oracle.</summary>
    /// <param name="kind">The actual node key transport.</param>
    [Theory]
    [InlineData(NativeGuardKeyKind.NativeNumber)]
    [InlineData(NativeGuardKeyKind.NativeIdentifier)]
    [InlineData(NativeGuardKeyKind.OrdinalText)]
    [InlineData(NativeGuardKeyKind.ConvertedInteger)]
    public async Task ProtectTrackedIdentities_AllKeyShapes_PreserveUnrelatedRows(NativeGuardKeyKind kind)
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS");
        new BenchmarkRunOptions { Engine = BenchmarkEngine.SqliteMemory }.ApplyEnvironment();
        var benchmark = new NativeGuardBenchmarks { Case = new(kind, 3, 1, true) };

        try
        {
            benchmark.Initialize();
            await benchmark.PrepareAsync(TestContext.Current.CancellationToken);

            // Act
            await benchmark.ProtectTrackedIdentities();

            // Assert
            await benchmark.VerifyAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await benchmark.CleanupAsync();
            Environment.SetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS", previous);
        }
    }

    /// <summary>Both deletion and coordinated admission verification reject an omitted measured operation.</summary>
    /// <param name="delete">Whether the operation deletes a tree instead of admitting a guarded callback.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifyAsync_UnexecutedNativeGuard_RejectsNoOp(bool delete)
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS");
        new BenchmarkRunOptions { Engine = BenchmarkEngine.SqliteMemory }.ApplyEnvironment();
        var benchmark = new NativeGuardBenchmarks { Case = new(NativeGuardKeyKind.NativeNumber, 1, 1, delete) };

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

    /// <summary>A mutation admission retains manual detection and rejects missing measured work.</summary>
    [Fact]
    public async Task AdmitCleanTracker_ActualBoundary_PreservesManualDetection()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS");
        new BenchmarkRunOptions { Engine = BenchmarkEngine.SqliteMemory }.ApplyEnvironment();
        var benchmark = new CleanMutationBoundaryBenchmarks { Tracked = 20 };

        try
        {
            await benchmark.PrepareAsync(TestContext.Current.CancellationToken);

            // Act
            await benchmark.AdmitCleanTracker();

            // Assert
            await benchmark.VerifyAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await benchmark.CleanupAsync();
            Environment.SetEnvironmentVariable("NESTEDSET_BENCHMARK_OPTIONS", previous);
        }
    }

    /// <summary>All former dense native construction profiles retain deferred provider expressions.</summary>
    /// <param name="engine">The native dialect used to finalize metadata.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    public async Task Construct_DensePathProfiles_RemainsDeferred(BenchmarkEngine engine)
    {
        // Arrange
        var benchmark = new DensePathConstructionBenchmarks { Engine = engine };
        benchmark.Initialize();

        try
        {
            // Act
            var queries = DensePathConstructionBenchmarks.Cases.Select(value =>
            {
                benchmark.Case = value;

                return benchmark.Construct();
            }).ToArray();

            // Assert
            Assert.Equal(24, queries.Length);
            Assert.All(queries, query => Assert.IsAssignableFrom<IQueryable>(query));
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Complete indexed fact capture and post-composition consumer variants remain deferred.</summary>
    /// <param name="engine">The native dialect used to finalize indexed metadata.</param>
    [Theory]
    [InlineData(BenchmarkEngine.MySql)]
    [InlineData(BenchmarkEngine.MariaDb)]
    public async Task Construct_IndexedFactProfiles_RemainsDeferred(BenchmarkEngine engine)
    {
        // Arrange
        var benchmark = new AncestorFactConstructionBenchmarks { Engine = engine };
        benchmark.Initialize();

        try
        {
            // Act
            var queries = NativeFactCase.All.Select(value =>
            {
                benchmark.Case = value;

                return benchmark.Construct();
            }).ToArray();

            // Assert
            Assert.Equal(18, queries.Length);
            Assert.All(queries, query => Assert.IsAssignableFrom<IQueryable>(query));
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }
}
