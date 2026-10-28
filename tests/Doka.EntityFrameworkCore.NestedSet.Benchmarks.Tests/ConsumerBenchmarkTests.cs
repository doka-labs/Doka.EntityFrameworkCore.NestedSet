using SelectionStatus = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups.SupervisorResolutionStatus;
using SignalData = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.SignalReportDataset;
using SignalSource = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.CapturePort;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Checks actual consumer lifecycles and complete references before observational timing.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class ConsumerBenchmarkTests
{
    /// <summary>Neither consumer may verify successfully when the measured operation was never invoked.</summary>
    /// <param name="groups">Whether the actual group-selection or signal reporting lifecycle is prepared.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumerVerificationRejectsUnexecutedOperation(
        bool groups
    )
    {
        // Arrange
        IDatabaseBenchmark benchmark = groups
            ? new UserGroupSelectionBenchmarks
            {
                Nodes = 32,
                Shape = BenchmarkShape.Wide,
                Trees = 1,
                Origins = 1,
                Matches = 1,
            }
            : new SignalReportingBenchmarks
            {
                Nodes = 40,
                Shape = BenchmarkShape.Wide,
                Trees = 3,
            };

        try
        {
            benchmark.Initialize();
            await benchmark.PrepareAsync();

            // Act
            var error = await Record.ExceptionAsync(benchmark.VerifyAsync);

            // Assert
            Assert.IsType<InvalidOperationException>(error);
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Rejects impossible origin populations before the existing launcher can acquire a database.</summary>
    /// <param name="origins">The exact native origin parameter selected from the benchmark matrix.</param>
    /// <param name="valid">Whether that many visible memberships can exist in the prepared group population.</param>
    [Theory]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(100, false)]
    public void NativePreflightValidatesConsumerOriginPopulation(
        int origins,
        bool valid
    )
    {
        // Arrange
        const string variable = "NESTEDSET_BENCHMARK_OPTIONS";
        var previous = Environment.GetEnvironmentVariable(variable);

        try
        {
            var options = new BenchmarkRunOptions
            {
                NodeCounts = [32],
                Shapes = [BenchmarkShape.Wide],
            };

            options.ApplyEnvironment();
            var configuration = BenchmarkConfiguration.Create(options);
            using var cases = BenchmarkConverter.TypeToBenchmarks(typeof(UserGroupSelectionBenchmarks), configuration);
            var selected = cases.BenchmarksCases.Single(item => item.Descriptor.WorkloadMethod.Name == "Api"
                && (int)item.Parameters[nameof(UserGroupSelectionBenchmarks.Origins)] == origins
                && (int)item.Parameters[nameof(UserGroupSelectionBenchmarks.Matches)] == 1
                && (BenchmarkPersonSelection)item.Parameters[nameof(UserGroupSelectionBenchmarks.Selection)]
                == BenchmarkPersonSelection.RequireUnique);

            var exact = ManualConfig
                .Create(configuration)
                .AddFilter(new GlobFilter([FullNameProvider.GetBenchmarkName(selected)]));

            // Act
            var error = Record.Exception(() =>
            {
                using var result = BenchmarkSelection.Discover(exact, diagnostics: true);
            });

            // Assert
            if (valid)
            {
                Assert.Null(error);
            }
            else
            {
                Assert.Contains(
                    "Origins <= Nodes",
                    Assert.IsType<ArgumentException>(error).Message,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    /// <summary>Actual sample families participate in the existing resource preflight and native diagnostics.</summary>
    /// <param name="type">The real signal or group-selection family.</param>
    [Theory]
    [InlineData(typeof(SignalReportingBenchmarks))]
    [InlineData(typeof(UserGroupSelectionBenchmarks))]
    public void ConsumerSelectionRequiresTheActualDatabaseLifecycle(
        Type type
    )
    {
        // Arrange
        var configuration = ManualConfig
            .Create(BenchmarkConfiguration.Create(new BenchmarkRunOptions()))
            .AddFilter(new GlobFilter(["*" + type.Name + ".*"]));

        // Act
        using var selected = BenchmarkSelection.Discover(configuration, diagnostics: true);

        // Assert
        Assert.True(selected.RequiresDatabase);
        Assert.NotEmpty(selected.DatabaseCases);
        Assert.All(selected.DatabaseCases, item => Assert.Equal(type, item.Descriptor.Type));
    }

    /// <summary>Native generation accepts actual-model lifecycles without an unused generic fixture.</summary>
    /// <param name="type">The actual consumer family.</param>
    [Theory]
    [InlineData(typeof(SignalReportingBenchmarks))]
    [InlineData(typeof(UserGroupSelectionBenchmarks))]
    public void NativeValidationAcceptsActualConsumerFamilies(
        Type type
    )
    {
        // Arrange
        using var discovery = BenchmarkConverter.TypeToBenchmarks(
            type,
            BenchmarkConfiguration.Create(new BenchmarkRunOptions()));

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
        Assert.False(typeof(DatabaseBenchmark).IsAssignableFrom(type));
    }

    /// <summary>Both signal queries retain source, catalog, periods, metadata and missing-cell semantics.</summary>
    /// <param name="reference">Whether ordinary mapping-specific LINQ is measured.</param>
    /// <param name="nearest">Whether metadata uses nearest-before-value rather than Any.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ActualSignalReportMatchesItsCompleteReference(
        bool reference,
        bool nearest
    )
    {
        // Arrange
        await using var benchmark = new SignalReportingBenchmarks
        {
            Nodes = 40,
            Shape = BenchmarkShape.Balanced,
            Trees = 3,
            Tracked = 0,
            NearestMetadata = nearest,
        };

        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        if (reference)
        {
            await benchmark.Reference();
        }
        else
        {
            await benchmark.Api();
        }

        // Assert
        await benchmark.VerifyAsync();
        Assert.Equal(24, benchmark.Result.Count);
        Assert.Contains(benchmark.Result, cell => cell.AnchorId == SignalData.Id(1)
            && cell.ChannelId == SignalData.PulseChannel && cell.Source == SignalSource.Pulse
            && cell is { HasData: true, Value: 70 + 40 - SignalData.SeedNodeCount });
    }

    /// <summary>Server reduction and application reduction agree on complete origin and evidence outcomes.</summary>
    /// <param name="reference">Whether all candidates are reduced with the same pure application rules.</param>
    /// <param name="selection">The person-selection rule whose ties and missing results must be preserved.</param>
    [Theory]
    [InlineData(false, BenchmarkPersonSelection.RequireUnique)]
    [InlineData(true, BenchmarkPersonSelection.RequireUnique)]
    [InlineData(false, BenchmarkPersonSelection.HighestBusinessPriority)]
    [InlineData(true, BenchmarkPersonSelection.HighestBusinessPriority)]
    [InlineData(false, BenchmarkPersonSelection.SecondPerson)]
    [InlineData(true, BenchmarkPersonSelection.SecondPerson)]
    public async Task ActualGroupSelectionMatchesItsCompleteReference(
        bool reference,
        BenchmarkPersonSelection selection
    )
    {
        // Arrange
        await using var benchmark = new UserGroupSelectionBenchmarks
        {
            Nodes = 32,
            Shape = BenchmarkShape.Balanced,
            Trees = 1,
            Tracked = 0,
            Origins = 16,
            Matches = 4,
            Selection = selection,
        };

        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        if (reference)
        {
            await benchmark.Reference();
        }
        else
        {
            await benchmark.Api();
        }

        // Assert
        await benchmark.VerifyAsync();
        Assert.Equal(16, benchmark.Result.Origins.Count);
        Assert.True(benchmark.Result.Complete);
    }

    /// <summary>Prepared execution preserves complete scalar statuses and excludes setup command counts.</summary>
    /// <param name="selection">The complete person-ranking semantics used by the prepared scalar query.</param>
    [Theory]
    [InlineData(BenchmarkPersonSelection.RequireUnique)]
    [InlineData(BenchmarkPersonSelection.HighestBusinessPriority)]
    [InlineData(BenchmarkPersonSelection.SecondPerson)]
    public async Task PreparedGroupExecutionMatchesItsCompleteReference(BenchmarkPersonSelection selection)
    {
        // Arrange
        const string variable = "NESTEDSET_BENCHMARK_OPTIONS";
        var previous = Environment.GetEnvironmentVariable(variable);

        try
        {
            // WHY: Command observation is deliberately absent during timed BDN runs. This resource-owned
            // contract test opts into the existing untimed diagnostic mode without changing that boundary.
            var options = BenchmarkRunOptions.Current with { Diagnostics = true };
            options.ApplyEnvironment();
            await using var benchmark = new UserGroupSelectionBenchmarks
            {
                Nodes = 32,
                Shape = BenchmarkShape.Balanced,
                Trees = 1,
                Tracked = 0,
                Origins = 32,
                Matches = 4,
                Selection = selection,
            };

            benchmark.Initialize();
            await benchmark.PrepareAsync();
            var observation = ((IDatabaseBenchmark)benchmark).Observation;
            var preparationCommands = observation.Commands;

            // Act
            await benchmark.Prepared();
            var operationCommands = observation.Commands;

            // Assert
            await benchmark.VerifyAsync();
            Assert.Equal(0, preparationCommands);
            Assert.Equal(1, operationCommands);
            Assert.Equal(32, benchmark.Result.Origins.Count);
            Assert.True(benchmark.Result.Complete);
            Assert.Contains(benchmark.Result.Origins, origin => origin.Status == SelectionStatus.NotFound);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    /// <summary>Prepared execution cannot silently build an unprepared query inside its measured operation.</summary>
    [Fact]
    public async Task PreparedGroupExecutionRejectsMissingPreparation()
    {
        // Arrange
        await using var benchmark = new UserGroupSelectionBenchmarks();

        // Act
        var error = await Record.ExceptionAsync(benchmark.Prepared);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>The origin population includes a root whose explicit supervisor outcome is NotFound.</summary>
    [Fact]
    public async Task EveryVisibleGroupCanBeAnOriginIncludingRoot()
    {
        // Arrange
        await using var benchmark = new UserGroupSelectionBenchmarks
        {
            Nodes = 32,
            Shape = BenchmarkShape.Wide,
            Trees = 1,
            Tracked = 0,
            Origins = 32,
            Matches = 1,
            Selection = BenchmarkPersonSelection.HighestBusinessPriority,
        };

        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        await benchmark.Api();

        // Assert
        await benchmark.VerifyAsync();
        Assert.Equal(32, benchmark.Result.Origins.Count);
        Assert.Contains(benchmark.Result.Origins, origin => origin.Status == SelectionStatus.NotFound);
    }
}
