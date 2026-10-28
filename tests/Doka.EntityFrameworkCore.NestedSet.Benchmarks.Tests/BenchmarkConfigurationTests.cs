using Perfolizer.Mathematics.OutlierDetection;
using BenchmarkRunMode = BenchmarkDotNet.Jobs.RunMode;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Guards process isolation, one-operation iterations and observational export contracts.</summary>
public sealed class BenchmarkConfigurationTests
{
    /// <summary>Database mutations use one invocation and no unrolling against each restored state.</summary>
    [Fact]
    public void DatabaseJobRunsOncePerIteration()
    {
        // Arrange
        var configuration = new DatabaseBenchmarkConfiguration();

        // Act
        var jobs = configuration
            .GetJobs()
            .ToArray();

        // Assert
        var job = Assert.Single(jobs);
        Assert.Equal(1L, job.Run.InvocationCount);
        Assert.Equal(1, job.Run.UnrollFactor);
        Assert.Equal("Release", job.Infrastructure.BuildConfiguration);
        Assert.False(
            job.Infrastructure.HasValue(InfrastructureMode.ToolchainCharacteristic)
            && job.Infrastructure.Toolchain.IsInProcess);
        Assert.Equal(3, job.Run.WarmupCount);
        Assert.Equal(12, job.Run.IterationCount);
    }

    /// <summary>
    /// Framework iteration delegates synchronously complete the awaited preparation and verification.
    /// </summary>
    [Fact]
    public void IterationHooksExposeSynchronousActions()
    {
        // Arrange
        var type = typeof(DatabaseBenchmark);

        // Act
        var setup = type
            .GetMethods()
            .Single(method => method.IsDefined(typeof(IterationSetupAttribute)));

        var cleanup = type
            .GetMethods()
            .Single(method => method.IsDefined(typeof(IterationCleanupAttribute)));

        // Assert
        Assert.Equal(typeof(void), setup.ReturnType);
        Assert.Equal(typeof(void), cleanup.ReturnType);
        Assert.Empty(setup.GetParameters());
        Assert.Empty(cleanup.GetParameters());
        Assert.Equal(nameof(DatabaseBenchmark.Prepare), setup.Name);
        Assert.Equal(nameof(DatabaseBenchmark.Verify), cleanup.Name);
    }

    /// <summary>
    /// Results retain raw measurements and allocation diagnostics without handwritten acceptance ceilings.
    /// </summary>
    [Fact]
    public void ObservationConfigExportsMeasurementsWithoutBudgetTypes()
    {
        // Arrange
        var options = new BenchmarkRunOptions();

        // Act
        var configuration = BenchmarkConfiguration.Create(options);

        // Assert
        Assert.Equal(options.ArtifactsPath, configuration.ArtifactsPath);
        Assert.Contains(configuration.GetExporters(), exporter => exporter is JsonExporter);
        Assert.Contains(configuration.GetExporters(), exporter => exporter is CsvMeasurementsExporter);
        Assert.Contains(configuration.GetDiagnosers(), diagnoser => diagnoser is MemoryDiagnoser);
        Assert.Null(
            typeof(DatabaseBenchmark).Assembly.GetType("Doka.EntityFrameworkCore.NestedSet.Benchmarks.MutationBudgets"));
        Assert.Null(
            typeof(DatabaseBenchmark).Assembly.GetType("Doka.EntityFrameworkCore.NestedSet.Benchmarks.BenchmarkMeasurement"));
    }

    /// <summary>Read-only signal families leave warmup, invocation calibration and precision stopping adaptive.</summary>
    /// <param name="type">The expression-construction or complete-read family.</param>
    [Theory]
    [InlineData(typeof(SignalReportConstructionBenchmarks))]
    [InlineData(typeof(SignalReportingBenchmarks))]
    public void SignalFamiliesPreserveAdaptiveMeasurementStages(
        Type type
    )
    {
        // Arrange
        var configuration = BenchmarkConfiguration.Create(new BenchmarkRunOptions());

        // Act
        using var discovery = BenchmarkConverter.TypeToBenchmarks(type, configuration);

        // Assert
        Assert.NotEmpty(discovery.BenchmarksCases);
        Assert.All(discovery.BenchmarksCases, benchmark =>
        {
            Assert.Null(benchmark.Descriptor.IterationSetupMethod);
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.InvocationCountCharacteristic));
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.IterationTimeCharacteristic));
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.WarmupCountCharacteristic));
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.IterationCountCharacteristic));
        });
    }

    /// <summary>
    /// Native CLI accuracy bounds reach signal jobs without replacing adaptive stages with fixed counts.
    /// </summary>
    /// <param name="type">The expression-construction or complete-read family.</param>
    [Theory]
    [InlineData(typeof(SignalReportConstructionBenchmarks))]
    [InlineData(typeof(SignalReportingBenchmarks))]
    public void NativeAdaptiveProtocolPreservesEffectiveSignalJobSettings(
        Type type
    )
    {
        // Arrange
        var configuration = BenchmarkConfiguration.Create(new BenchmarkRunOptions());
        string[] arguments =
        [
            "--launchCount", "3",
            "--minWarmupCount", "20",
            "--maxWarmupCount", "80",
            "--minIterationCount", "20",
            "--maxIterationCount", "100",
            "--outliers", "DontRemove",
        ];

        // Act
        var (isSuccess, config, _) = ConfigParser.Parse(arguments, NullLogger.Instance, configuration);
        using var discovery = BenchmarkConverter.TypeToBenchmarks(
            type,
            ManualConfig.Union(configuration, config));

        // Assert
        Assert.True(isSuccess);
        Assert.NotEmpty(discovery.BenchmarksCases);
        Assert.All(discovery.BenchmarksCases, benchmark =>
        {
            Assert.Equal(3, benchmark.Job.Run.LaunchCount);
            Assert.Equal(20, benchmark.Job.Run.MinWarmupIterationCount);
            Assert.Equal(80, benchmark.Job.Run.MaxWarmupIterationCount);
            Assert.Equal(20, benchmark.Job.Run.MinIterationCount);
            Assert.Equal(100, benchmark.Job.Run.MaxIterationCount);
            Assert.Equal(OutlierMode.DontRemove, benchmark.Job.Accuracy.OutlierMode);
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.InvocationCountCharacteristic));
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.IterationTimeCharacteristic));
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.WarmupCountCharacteristic));
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.IterationCountCharacteristic));
        });
    }

    /// <summary>
    /// Invalid adaptive iteration ranges remain native configuration errors before any workload runs.
    /// </summary>
    /// <param name="type">The expression-construction or complete-read family.</param>
    /// <param name="warmup">Whether the invalid bounds target warmup or measurement iterations.</param>
    [Theory]
    [InlineData(typeof(SignalReportConstructionBenchmarks), false)]
    [InlineData(typeof(SignalReportConstructionBenchmarks), true)]
    [InlineData(typeof(SignalReportingBenchmarks), false)]
    [InlineData(typeof(SignalReportingBenchmarks), true)]
    public void NativeValidationRejectsReversedAdaptiveRanges(
        Type type,
        bool warmup
    )
    {
        // Arrange
        var configuration = BenchmarkConfiguration.Create(new BenchmarkRunOptions());
        string[] arguments = warmup
            ? ["--minWarmupCount", "20", "--maxWarmupCount", "10"]
            : ["--minIterationCount", "20", "--maxIterationCount", "10"];

        var (isSuccess, config, _) = ConfigParser.Parse(arguments, NullLogger.Instance, configuration);
        using var discovery = BenchmarkConverter.TypeToBenchmarks(
            type, ManualConfig.Union(configuration, config));

        var validation = new ValidationParameters(discovery.BenchmarksCases, discovery.Config);
        var maximum = warmup ? "MaxWarmupIterationCount" : "MaxIterationCount";

        // Act
        var errors = discovery.Config.GetValidators().SelectMany(validator => validator.Validate(validation))
            .ToArray();

        // Assert
        Assert.True(isSuccess);
        Assert.Contains(errors, error => error.IsCritical
            && error.Message.Contains(maximum, StringComparison.Ordinal)
            && error.Message.Contains("must be greater than", StringComparison.Ordinal));
    }

    /// <summary>The pure core benchmark consumes every prepared calculation result.</summary>
    [Fact]
    public void CoreBoundsBenchmarkReportsAllContainedLeaves()
    {
        // Arrange
        var benchmark = new BoundsBenchmarks { Nodes = 100 };
        benchmark.Prepare();

        // Act
        var count = benchmark.CountContainedLeaves();

        // Assert
        Assert.Equal(100, count);
    }

    /// <summary>Unsupported process or iteration overrides fail before any database operation is measured.</summary>
    /// <param name="overrideName">The incompatible framework job override.</param>
    /// <param name="message">The critical validation explanation.</param>
    [Theory]
    [InlineData("Debug", "Release")]
    [InlineData("Invocation", "invocation count and unroll factor")]
    [InlineData("Unroll", "invocation count and unroll factor")]
    [InlineData("InProcess", "isolated child process")]
    public void ConfigurationRejectsInvalidMeasurementOverrides(
        string overrideName,
        string message
    )
    {
        // Arrange
        var job = Job.Default.RunOncePerIteration();
        job = overrideName switch
        {
            "Debug" => job.WithCustomBuildConfiguration("Debug"),
            "Invocation" => job.WithInvocationCount(2),
            "Unroll" => job.WithUnrollFactor(2),
            "InProcess" => job.WithToolchain(InProcessEmitToolchain.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(overrideName)),
        };
        var configuration = ManualConfig
            .Create(BenchmarkConfiguration.Create(new BenchmarkRunOptions()))
            .AddJob(job);

        using var discovery = BenchmarkConverter.TypeToBenchmarks(typeof(InsertBenchmarks), configuration);
        var validation = new ValidationParameters(discovery.BenchmarksCases, discovery.Config);

        // Act
        var errors = discovery
            .Config
            .GetValidators()
            .SelectMany(validator => validator.Validate(validation))
            .ToArray();

        // Assert
        Assert.Contains(errors, error => error.IsCritical && error.Message.Contains(message, StringComparison.Ordinal));
    }
}
