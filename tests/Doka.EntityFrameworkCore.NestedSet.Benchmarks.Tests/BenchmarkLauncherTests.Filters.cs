namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class BenchmarkLauncherTests
{
    /// <summary>An unmatched filter fails before resources or an observation sidecar are acquired.</summary>
    [Fact]
    public async Task Main_UnmatchedDiagnosticFilter_FailsBeforeResourceAcquisition()
    {
        // Arrange
        using var process = new LauncherProcessState();
        const string previousConnection = "unmatched-selection-must-not-acquire-a-connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previousConnection);
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://127.0.0.1:1");
        var artifacts = Path.Combine(process.Directory, "unmatched");
        string[] arguments =
        [
            "--engine=MySql",
            "--nodes=20",
            "--diagnostics",
            "--artifacts",
            artifacts,
            "--filter=*MissingBenchmarkOperation",
        ];

        // Act
        var exitCode = await Program.Main(arguments);

        // Assert
        Assert.Equal(2, exitCode);
        Assert.False(Directory.Exists(artifacts));
        Assert.Equal(previousConnection, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.Contains("No benchmark matched the native selection.", process.Error, StringComparison.Ordinal);
    }

    /// <summary>A native exact-case filter excludes invalid combinations from a mixed dataset matrix.</summary>
    [Fact]
    public async Task Main_UnselectedInvalidScenarios_DoNotPreventValidDiagnostics()
    {
        // Arrange
        using var process = new LauncherProcessState();
        var artifacts = Path.Combine(process.Directory, "mixed-matrix");
        var options = new BenchmarkRunOptions
        {
            NodeCounts = [10, 40],
            Shapes = [BenchmarkShape.Wide, BenchmarkShape.Deep],
            TreeCounts = [1, 4],
            ArtifactsPath = artifacts,
        };

        options.ApplyEnvironment();

        using var discovery = BenchmarkConverter.TypeToBenchmarks(
            typeof(InsertBenchmarks),
            BenchmarkConfiguration.Create(options));

        var selected = discovery.BenchmarksCases.Single(benchmark =>
            benchmark.Descriptor.WorkloadMethod.Name == nameof(InsertBenchmarks.Root)
            && HasParameters(benchmark, 40, BenchmarkShape.Deep, 4, 0));

        var filter = FullNameProvider.GetBenchmarkName(selected);
        string[] arguments =
        [
            "--engine=SqliteMemory",
            "--nodes=10,40",
            "--shape=Wide,Deep",
            "--trees=1,4",
            "--diagnostics",
            "--artifacts",
            artifacts,
            "--filter",
            filter,
        ];

        // Act
        var exitCode = await Program.Main(arguments);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains(
            discovery.BenchmarksCases,
            benchmark => HasParameters(benchmark, 10, BenchmarkShape.Deep, 4, 0));

        using var diagnostics = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(artifacts, "command-diagnostics.json"), CancellationToken.None));

        var result = Assert.Single(
            diagnostics
                .RootElement
                .GetProperty("Results")
                .EnumerateArray());

        Assert.EndsWith(
            ".InsertBenchmarks.Root",
            result
                .GetProperty("Benchmark")
                .GetString());

        Assert.Equal(
            40,
            result
                .GetProperty("Nodes")
                .GetInt32());

        Assert.Equal(
            (int)BenchmarkShape.Deep,
            result
                .GetProperty("Shape")
                .GetInt32());

        Assert.Equal(
            4,
            result
                .GetProperty("Trees")
                .GetInt32());
    }

    /// <summary>A native full-case filter selects the requested dataset from a larger parameter matrix.</summary>
    [Fact]
    public async Task Main_DiagnosticFilter_HonorsNativeParameterSelection()
    {
        // Arrange
        using var process = new LauncherProcessState();
        var artifacts = Path.Combine(process.Directory, "parameters");
        var options = new BenchmarkRunOptions
        {
            NodeCounts = [20, 40],
            Shapes = [BenchmarkShape.Wide, BenchmarkShape.Deep],
            TreeCounts = [1, 2],
            TrackedCounts = [0, 3],
            ArtifactsPath = artifacts,
        };

        options.ApplyEnvironment();

        using var discovery = BenchmarkConverter.TypeToBenchmarks(
            typeof(InsertBenchmarks),
            BenchmarkConfiguration.Create(options));

        var selected = discovery.BenchmarksCases.Single(benchmark =>
            benchmark.Descriptor.WorkloadMethod.Name == nameof(InsertBenchmarks.Root)
            && HasParameters(benchmark, 40, BenchmarkShape.Deep, 2, 3));

        var filter = FullNameProvider.GetBenchmarkName(selected);
        string[] arguments =
        [
            "--engine=SqliteMemory",
            "--nodes=20,40",
            "--shape=Wide,Deep",
            "--trees=1,2",
            "--tracked=0,3",
            "--diagnostics",
            "--artifacts",
            artifacts,
            "--filter",
            filter,
        ];

        // Act
        var exitCode = await Program.Main(arguments);

        // Assert
        Assert.Equal(0, exitCode);

        using var diagnostics = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(artifacts, "command-diagnostics.json"), CancellationToken.None));

        var result = Assert.Single(
            diagnostics
                .RootElement
                .GetProperty("Results")
                .EnumerateArray());

        Assert.EndsWith(
            ".InsertBenchmarks.Root",
            result
                .GetProperty("Benchmark")
                .GetString());

        Assert.Equal(
            40,
            result
                .GetProperty("Nodes")
                .GetInt32());

        Assert.Equal(
            (int)BenchmarkShape.Deep,
            result
                .GetProperty("Shape")
                .GetInt32());

        Assert.Equal(
            2,
            result
                .GetProperty("Trees")
                .GetInt32());

        Assert.Equal(
            3,
            result
                .GetProperty("Tracked")
                .GetInt32());
    }

    /// <summary>Distinct framework jobs share one untimed diagnostic per operation and dataset.</summary>
    [Fact]
    public async Task RunAsync_Diagnostics_DeduplicateNativeJobs()
    {
        // Arrange
        using var process = new LauncherProcessState();
        var options = new BenchmarkRunOptions
        {
            NodeCounts = [20],
            ArtifactsPath = Path.Combine(process.Directory, "jobs"),
            Diagnostics = true,
        };

        options.ApplyEnvironment();

        await using var environment = await BenchmarkEnvironment.CreateAsync(options, CancellationToken.None);
        var configuration = ManualConfig
            .Create(BenchmarkConfiguration.Create(options))
            .AddJob(
                Job
                    .Default
                    .RunOncePerIteration()
                    .WithGcServer(false)
                    .WithId("diagnostic-workstation"))
            .AddJob(
                Job
                    .Default
                    .RunOncePerIteration()
                    .WithGcServer(true)
                    .WithId("diagnostic-server"))
            .AddFilter(new GlobFilter(["*InsertBenchmarks.Root"]));

        using var discovery = BenchmarkConverter.TypeToBenchmarks(typeof(InsertBenchmarks), configuration);
        var nativeCaseCount = discovery.BenchmarksCases.Length;
        using var selection = BenchmarkSelection.Discover(configuration, true);

        // Act
        await BenchmarkDiagnostics.RunAsync(options, selection, CancellationToken.None);

        // Assert
        Assert.True(nativeCaseCount > 1);

        using var diagnostics = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(options.ArtifactsPath, "command-diagnostics.json"),
                CancellationToken.None));

        var result = Assert.Single(
            diagnostics
                .RootElement
                .GetProperty("Results")
                .EnumerateArray());

        Assert.EndsWith(
            ".InsertBenchmarks.Root",
            result
                .GetProperty("Benchmark")
                .GetString());

        Assert.Equal(
            20,
            result
                .GetProperty("Nodes")
                .GetInt32());

        Assert.True(
            result
                .GetProperty("Commands")
                .GetInt32()
            > 0);
    }
}
