using System.Globalization;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using BenchmarkRunMode = BenchmarkDotNet.Jobs.RunMode;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Guards adaptive, read-only signal measurements independently of mutation iteration resets.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class SignalReadBenchmarkTests
{
    /// <summary>Repeated complete reports preserve their fixture, scalar cells and empty tracker.</summary>
    /// <param name="reference">Whether the explicit LINQ reference is repeatedly measured.</param>
    /// <param name="nearest">Whether metadata uses nearest-before-value rather than Any.</param>
    /// <param name="diagnostics">Whether the untimed command observer is explicitly enabled.</param>
    /// <param name="irrelevantChoices">The valid catalog choices not selected by the report.</param>
    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(false, true, false, 0)]
    [InlineData(true, false, false, 0)]
    [InlineData(true, true, false, 0)]
    [InlineData(false, false, true, 1000)]
    [InlineData(false, true, true, 1000)]
    [InlineData(true, false, true, 1000)]
    [InlineData(true, true, true, 1000)]
    public async Task RepeatedReportsKeepPreparedDataAndOperationOnlyObservations(
        bool reference,
        bool nearest,
        bool diagnostics,
        int irrelevantChoices
    )
    {
        // Arrange
        const string variable = "NESTEDSET_BENCHMARK_OPTIONS";
        var previous = Environment.GetEnvironmentVariable(variable);

        try
        {
            var options = BenchmarkRunOptions.Current with { Diagnostics = diagnostics };
            options.ApplyEnvironment();
            await using var benchmark = new SignalReportingBenchmarks
            {
                Nodes = 40,
                Shape = BenchmarkShape.Wide,
                Trees = 3,
                Tracked = 0,
                NearestMetadata = nearest,
                IrrelevantChoices = irrelevantChoices,
            };

            await benchmark.SetupAsync();
            var observation = ((IDatabaseBenchmark)benchmark).Observation;
            var preparationCommands = observation.Commands;
            var writesBefore = await ReadConnectionWritesAsync(benchmark.Context);
            var reports = new SignalReportCell[3][];

            // Act
            for (var index = 0; index < reports.Length; index++)
            {
                if (reference)
                {
                    await benchmark.Reference();
                }
                else
                {
                    await benchmark.Api();
                }

                reports[index] = benchmark.Result.ToArray();
            }

            // Assert
            Assert.Equal(0, preparationCommands);
            Assert.Equal(diagnostics ? 6 : 0, observation.Commands);
            Assert.Equal(0, observation.Updates);
            Assert.Equal(writesBefore, await ReadConnectionWritesAsync(benchmark.Context));
            Assert.Empty(benchmark.Context.ChangeTracker.Entries());
            Assert.All(reports, report =>
            {
                Assert.Equal(24, report.Length);
                Assert.Equal(reports[0], report);
                Assert.Contains(report, cell => cell.AnchorId == SignalReportDataset.Id(1)
                    && cell.ChannelId == SignalReportDataset.PulseChannel && cell.Source == CapturePort.Pulse
                    && cell is { HasData: true, Value: 70 + 40 - SignalReportDataset.SeedNodeCount });
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    /// <summary>Read-only signal jobs leave invocation calibration to BenchmarkDotNet.</summary>
    [Fact]
    public void SignalLifecyclePermitsAdaptiveThroughput()
    {
        // Arrange
        var configuration = BenchmarkConfiguration.Create(new BenchmarkRunOptions());
        using var discovery = BenchmarkConverter.TypeToBenchmarks(typeof(SignalReportingBenchmarks), configuration);

        // Act
        var cases = discovery.BenchmarksCases;

        // Assert
        Assert.NotEmpty(cases);
        Assert.All(cases, benchmark =>
        {
            Assert.Null(benchmark.Descriptor.IterationSetupMethod);
            Assert.Null(benchmark.Descriptor.IterationCleanupMethod);
            Assert.Equal(nameof(SignalReportingBenchmarks.SetupAsync), benchmark.Descriptor.GlobalSetupMethod?.Name);
            Assert.Equal(nameof(SignalReportingBenchmarks.VerifyAndCleanupAsync),
                benchmark.Descriptor.GlobalCleanupMethod?.Name);
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.InvocationCountCharacteristic));
            Assert.False(benchmark.Job.Run.HasValue(BenchmarkRunMode.UnrollFactorCharacteristic));
        });
    }

    /// <summary>Explicit multi-invocation read jobs do not inherit mutation-only validation failures.</summary>
    [Fact]
    public void SignalLifecycleAcceptsMultipleInvocationsPerIteration()
    {
        // Arrange
        var configuration = ManualConfig
            .Create(BenchmarkConfiguration.Create(new BenchmarkRunOptions()))
            .AddJob(
                Job
                    .Default
                    .WithInvocationCount(2)
                    .WithUnrollFactor(2));

        using var discovery = BenchmarkConverter.TypeToBenchmarks(typeof(SignalReportingBenchmarks), configuration);
        var validation = new ValidationParameters(discovery.BenchmarksCases, discovery.Config);

        // Act
        var failures = discovery
            .Config
            .GetValidators()
            .SelectMany(validator => validator.Validate(validation))
            .Where(error => error.IsCritical)
            .ToArray();

        // Assert
        Assert.Empty(failures);
        Assert.All(discovery.BenchmarksCases, benchmark => Assert.Equal(2L, benchmark.Job.Run.InvocationCount));
    }

    /// <summary>Final verification accepts completed reports and releases their globally prepared context.</summary>
    /// <param name="reference">Whether the completed measured read uses the explicit LINQ reference.</param>
    /// <param name="nearest">Whether metadata selects the nearest owner before checking its values.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GlobalCleanupVerifiesCompletedReadAndDisposesContext(
        bool reference,
        bool nearest
    )
    {
        // Arrange
        await using var benchmark = new SignalReportingBenchmarks
        {
            Nodes = 40,
            Shape = BenchmarkShape.Wide,
            Trees = 3,
            Tracked = 0,
            NearestMetadata = nearest,
        };

        await benchmark.SetupAsync();

        if (reference)
        {
            await benchmark.Reference();
        }
        else
        {
            await benchmark.Api();
        }

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAndCleanupAsync);

        // Assert
        Assert.Null(error);
        Assert.Equal(24, benchmark.Result.Count);
        Assert.Throws<InvalidOperationException>(() => benchmark.Context);
    }

    /// <summary>Failed final verification still disposes the globally prepared context.</summary>
    [Fact]
    public async Task GlobalCleanupRejectsUnexecutedReadAndDisposesContext()
    {
        // Arrange
        await using var benchmark = new SignalReportingBenchmarks
        {
            Nodes = 40,
            Shape = BenchmarkShape.Wide,
            Trees = 3,
            Tracked = 0,
        };

        await benchmark.SetupAsync();

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAndCleanupAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Throws<InvalidOperationException>(() => benchmark.Context);
    }

    /// <summary>Invalid catalog populations fail before a context or fixture is acquired.</summary>
    [Fact]
    public void InitializationRejectsNegativeCatalogPopulation()
    {
        // Arrange
        var benchmark = new SignalReportingBenchmarks
        {
            Trees = 3,
            IrrelevantChoices = -1,
        };

        // Act
        var error = Record.Exception(benchmark.Initialize);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(error);
        Assert.Throws<InvalidOperationException>(() => benchmark.Context);
    }

    /// <summary>Detects any persisted write on this SQLite connection without retaining entity histories.</summary>
    private static async Task<long> ReadConnectionWritesAsync(
        SignalLabContext context
    )
    {
        // WHY: A repeated report could preserve cardinality while modifying values. SQLite's cumulative
        // connection counter detects INSERT, UPDATE and DELETE independently of the report's own result.
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT total_changes()";
        var value = await command.ExecuteScalarAsync(CancellationToken.None);

        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
}
