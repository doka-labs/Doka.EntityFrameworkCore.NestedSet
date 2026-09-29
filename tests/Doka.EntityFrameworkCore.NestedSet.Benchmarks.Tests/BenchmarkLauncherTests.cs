using System.Globalization;
using FileDirectory = System.IO.Directory;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>
/// Verifies that native framework selection and artifact arguments reach the actual diagnostic launcher.
/// </summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class BenchmarkLauncherTests
{
    private const string ConnectionVariable = "NESTEDSET_BENCHMARK_CONNECTION_STRING";

#if DEBUG
    /// <summary>A Debug launcher rejects timing before acquiring a database or writing artifacts.</summary>
    /// <param name="filter">A database or Core measurement selection.</param>
    /// <remarks>WHY: The rejection exists only in Debug; Release executes the normal measurement path.</remarks>
    [Theory]
    [InlineData("*InsertBenchmarks.Root")]
    [InlineData("*BoundsBenchmarks.CountContainedLeaves")]
    public async Task DebugMeasurementsFailBeforeResourceAcquisition(
        string filter
    )
    {
        // Arrange
        using var process = new LauncherProcessState();
        const string previousConnection = "debug-measurement-must-not-acquire-a-connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previousConnection);
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://127.0.0.1:1");
        var artifacts = Path.Combine(process.Directory, "debug-measurement");
        string[] arguments =
        [
            "--engine=MySql",
            "--nodes=20",
            "--artifacts",
            artifacts,
            "--filter",
            filter,
        ];

        // Act
        var exitCode = await Program.Main(arguments);

        // Assert
        Assert.Equal(2, exitCode);
        Assert.False(Directory.Exists(artifacts));
        Assert.Equal(previousConnection, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.Contains(
            "Benchmark measurements require dotnet run -c Release.",
            process.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Benchmark execution failed.", process.Error, StringComparison.Ordinal);
    }

#endif
    /// <summary>
    /// Every native filter form selects only root insertion and co-locates diagnostics and provenance.
    /// </summary>
    /// <param name="form">The native filter syntax.</param>
    [Theory]
    [InlineData("uppercase")]
    [InlineData("equals")]
    [InlineData("short")]
    [InlineData("response")]
    public async Task DiagnosticsHonorNativeFiltersAndArtifacts(
        string form
    )
    {
        // Arrange
        using var process = new LauncherProcessState();
        var wrapperArtifacts = Path.Combine(process.Directory, "wrapper");
        var nativeArtifacts = Path.Combine(process.Directory, "native");
        var arguments = new List<string>
        {
            "--engine=SqliteMemory",
            "--nodes=20",
            "--diagnostics",
            "--artifacts",
            wrapperArtifacts,
        };
        switch (form)
        {
            case "uppercase":
                arguments.AddRange(["--FILTER", "*insertbenchmarks.root", "-a", nativeArtifacts]);
                break;
            case "equals":
                arguments.AddRange(["--filter=*InsertBenchmarks.Root", "-a", nativeArtifacts]);
                break;
            case "short":
                arguments.AddRange(["-f", "*InsertBenchmarks.Root", "-a", nativeArtifacts]);
                break;
            case "response":
                var response = Path.Combine(process.Directory, "selection.rsp");
                await File.WriteAllTextAsync(
                    response,
                    $"--filter\n*InsertBenchmarks.Root\n-a\n{nativeArtifacts}\n",
                    CancellationToken.None);
                arguments.Add("@" + response);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(form));
        }

        // Act
        var exitCode = await Program.Main(arguments.ToArray());

        // Assert
        Assert.Equal(0, exitCode);

        var diagnosticsPath = Path.Combine(nativeArtifacts, "command-diagnostics.json");
        var provenancePath = Path.Combine(nativeArtifacts, "provenance.json");
        Assert.True(File.Exists(diagnosticsPath));
        Assert.True(File.Exists(provenancePath));
        Assert.False(Directory.Exists(wrapperArtifacts));
        Assert.Equal(nativeArtifacts, BenchmarkRunOptions.Current.ArtifactsPath);

        using var diagnostics = JsonDocument.Parse(
            await File.ReadAllTextAsync(diagnosticsPath, CancellationToken.None));
        var result = Assert.Single(
            diagnostics
                .RootElement
                .GetProperty("Results")
                .EnumerateArray());

        Assert.EndsWith(".InsertBenchmarks.Root", result.GetProperty("Benchmark").GetString());
        Assert.Equal(20, result.GetProperty("Nodes").GetInt32());
        Assert.True(result.GetProperty("Commands").GetInt32() > 0);
        Assert.False(result.TryGetProperty("Milliseconds", out _));

        using var provenance = JsonDocument.Parse(await File.ReadAllTextAsync(provenancePath, CancellationToken.None));
        Assert.Equal("completed", provenance.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("diagnostics", provenance.RootElement.GetProperty("mode").GetString());
        Assert.Equal(
            nativeArtifacts,
            provenance
                .RootElement
                .GetProperty("settings")
                .GetProperty("artifactsPath")
                .GetString());
    }

    /// <summary>An unmatched filter fails before resources or an observation sidecar are acquired.</summary>
    [Fact]
    public async Task UnmatchedDiagnosticFilterFailsBeforeResourceAcquisition()
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

    /// <summary>
    /// Invalid selected forests, including the implicit second transfer tree, fail before Docker startup.
    /// </summary>
    /// <param name="nodes">The selected total node count.</param>
    /// <param name="trees">The requested independent tree count.</param>
    /// <param name="filter">The selected database operation.</param>
    /// <param name="diagnostics">Whether the launcher requests untimed diagnostics.</param>
    [Theory]
    [InlineData(20, 4, "*InsertBenchmarks.Root", false)]
    [InlineData(20, 4, "*InsertBenchmarks.Root", true)]
    [InlineData(10, 1, "*CrossTreeMoveBenchmarks.RootUnderOtherTree", false)]
    [InlineData(10, 1, "*CrossTreeMoveBenchmarks.RootUnderOtherTree", true)]
    public async Task InvalidSelectedScenarioFailsBeforeResourceAcquisition(
        int nodes,
        int trees,
        string filter,
        bool diagnostics
    )
    {
        // Arrange
        using var process = new LauncherProcessState();
        const string previousConnection = "invalid-selection-must-not-acquire-a-connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previousConnection);
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://127.0.0.1:1");
        var artifacts = Path.Combine(process.Directory, "invalid");
        var arguments = new List<string>
        {
            "--engine=MySql",
            $"--nodes={nodes}",
            $"--trees={trees}",
            "--artifacts",
            artifacts,
            "--filter",
            filter,
        };

        if (diagnostics)
        {
            arguments.Add("--diagnostics");
        }

        // Act
        var exitCode = await Program.Main(arguments.ToArray());

        // Assert
        Assert.Equal(2, exitCode);
        Assert.False(Directory.Exists(artifacts));
        Assert.Equal(previousConnection, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.Contains("nodes=", process.Error, StringComparison.Ordinal);
        Assert.Contains("trees=", process.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Docker", process.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A native exact-case filter excludes invalid combinations from a mixed dataset matrix.</summary>
    [Fact]
    public async Task UnselectedInvalidScenariosDoNotPreventValidDiagnostics()
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

        Assert.EndsWith(".InsertBenchmarks.Root", result.GetProperty("Benchmark").GetString());
        Assert.Equal(40, result.GetProperty("Nodes").GetInt32());
        Assert.Equal((int)BenchmarkShape.Deep, result.GetProperty("Shape").GetInt32());
        Assert.Equal(4, result.GetProperty("Trees").GetInt32());
    }

    /// <summary>The smallest valid transfer input executes with the native implicit second tree.</summary>
    [Fact]
    public async Task DiagnosticsHonorImplicitCrossTreeMinimum()
    {
        // Arrange
        using var process = new LauncherProcessState();
        var artifacts = Path.Combine(process.Directory, "minimum-transfer");
        string[] arguments =
        [
            "--engine=SqliteMemory",
            "--nodes=20",
            "--trees=1",
            "--diagnostics",
            "--artifacts",
            artifacts,
            "--filter=*CrossTreeMoveBenchmarks.RootUnderOtherTree",
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

        Assert.EndsWith(".CrossTreeMoveBenchmarks.RootUnderOtherTree", result.GetProperty("Benchmark").GetString());
        Assert.Equal(20, result.GetProperty("Nodes").GetInt32());
        Assert.Equal(2, result.GetProperty("Trees").GetInt32());
        Assert.True(result.GetProperty("Commands").GetInt32() > 0);
    }

    /// <summary>A native full-case filter selects the requested dataset from a larger parameter matrix.</summary>
    [Fact]
    public async Task DiagnosticFilterHonorsNativeParameterSelection()
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

        Assert.EndsWith(".InsertBenchmarks.Root", result.GetProperty("Benchmark").GetString());
        Assert.Equal(40, result.GetProperty("Nodes").GetInt32());
        Assert.Equal((int)BenchmarkShape.Deep, result.GetProperty("Shape").GetInt32());
        Assert.Equal(2, result.GetProperty("Trees").GetInt32());
        Assert.Equal(3, result.GetProperty("Tracked").GetInt32());
    }

    /// <summary>Distinct framework jobs share one untimed diagnostic per operation and dataset.</summary>
    [Fact]
    public async Task DiagnosticsDeduplicateNativeJobs()
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

        Assert.EndsWith(".InsertBenchmarks.Root", result.GetProperty("Benchmark").GetString());
        Assert.Equal(20, result.GetProperty("Nodes").GetInt32());
        Assert.True(result.GetProperty("Commands").GetInt32() > 0);
    }

    /// <summary>
    /// Listing resolves through native parsed settings before an unreachable Docker provider is acquired.
    /// </summary>
    /// <param name="responseFile">Whether the listing setting is supplied through a response file.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeListingDoesNotAcquireDatabase(
        bool responseFile
    )
    {
        // Arrange
        using var process = new LauncherProcessState();
        const string previousConnection = "listing-must-not-replace-the-inherited-connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previousConnection);
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://127.0.0.1:1");
        var artifacts = Path.Combine(process.Directory, "listing");
        var arguments = new List<string>
        {
            "--engine=MySql",
            "--artifacts",
            artifacts
        };

        if (responseFile)
        {
            var response = Path.Combine(process.Directory, "listing.rsp");
            await File.WriteAllTextAsync(response, "--LIST\nflat\n", CancellationToken.None);
            arguments.Add("@" + response);
        }
        else
        {
            arguments.AddRange(["--LIST", "flat"]);
        }

        // Act
        var exitCode = await Program.Main(arguments.ToArray());

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(previousConnection, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.False(Directory.Exists(artifacts));
        Assert.Contains(nameof(InsertBenchmarks.Root), process.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Benchmark execution failed.", process.Error, StringComparison.Ordinal);
    }

    /// <summary>Execution without a native selector lists available methods before acquiring resources.</summary>
    [Fact]
    public async Task MissingNativeSelectorFailsBeforeResourceAcquisition()
    {
        // Arrange
        using var process = new LauncherProcessState();
        const string previousConnection = "missing-selection-must-not-acquire-a-connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previousConnection);
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://127.0.0.1:1");
        var artifacts = Path.Combine(process.Directory, "missing-selector");
        string[] arguments = ["--engine=MySql", "--artifacts", artifacts];

        // Act
        var exitCode = await Program.Main(arguments);

        // Assert
        Assert.Equal(2, exitCode);
        Assert.Equal(previousConnection, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.False(Directory.Exists(artifacts));
        Assert.Contains(nameof(InsertBenchmarks.Root), process.Output, StringComparison.Ordinal);
        Assert.Contains("--filter", process.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Docker", process.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Both category selector forms, including native response expansion, count as explicit selection.
    /// </summary>
    /// <param name="selector">The native category selector.</param>
    /// <param name="responseFile">Whether native parsing reads the selector from a response file.</param>
    [Theory]
    [InlineData("--anyCategories", false)]
    [InlineData("--anyCategories", true)]
    [InlineData("--allCategories", false)]
    [InlineData("--allCategories", true)]
    public async Task NativeCategorySelectorsCountAsExplicitSelection(
        string selector,
        bool responseFile
    )
    {
        // Arrange
        using var process = new LauncherProcessState();
        var options = new BenchmarkRunOptions();
        string[] arguments = [selector, "SelectedCategory"];
        if (responseFile)
        {
            var response = Path.Combine(process.Directory, "categories.rsp");
            await File.WriteAllTextAsync(response, $"{selector}\nSelectedCategory\n", CancellationToken.None);
            arguments = ["@" + response];
        }

        var parsed = ConfigParser.Parse(arguments, ConsoleLogger.Default, BenchmarkConfiguration.Create(options));

        // Act
        var explicitSelection = BenchmarkSelection.HasExplicitSelection(parsed.options);

        // Assert
        Assert.True(parsed.isSuccess);
        Assert.True(explicitSelection);
        Assert.False(Directory.Exists(options.ArtifactsPath));
    }

    /// <summary>Core selection ignores unused database scenarios and never acquires an inherited provider.</summary>
    [Fact]
    public void CoreOnlyNativeSelectionRequiresNoDatabase()
    {
        // Arrange
        using var process = new LauncherProcessState();
        const string previousConnection = "core-selection-must-not-acquire-a-connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previousConnection);
        Environment.SetEnvironmentVariable("DOCKER_HOST", "tcp://127.0.0.1:1");
        var options = new BenchmarkRunOptions
        {
            Engine = BenchmarkEngine.MySql,
            NodeCounts = [10],
            TreeCounts = [4],
            ArtifactsPath = Path.Combine(process.Directory, "core"),
        };

        options.ApplyEnvironment();
        var configuration = ManualConfig
            .Create(BenchmarkConfiguration.Create(options))
            .AddFilter(new GlobFilter(["*BoundsBenchmarks.CountContainedLeaves"]));

        // Act
        using var selected = BenchmarkSelection.Discover(configuration, false);

        // Assert
        Assert.False(selected.RequiresDatabase);
        Assert.Empty(selected.DatabaseCases);
        Assert.Equal(previousConnection, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.False(Directory.Exists(options.ArtifactsPath));
        Assert.DoesNotContain("Docker", process.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads the framework's native parameter values when preparing one exact filter case.</summary>
    private static bool HasParameters(
        BenchmarkCase benchmark,
        int nodes,
        BenchmarkShape shape,
        int trees,
        int tracked
    )
    {
        var parameters = benchmark.Parameters.Items.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.Value);

        return Equals(nodes, parameters[nameof(DatabaseBenchmark.Nodes)])
            && Equals(shape, parameters[nameof(DatabaseBenchmark.Shape)])
            && Equals(trees, parameters[nameof(DatabaseBenchmark.Trees)])
            && Equals(tracked, parameters[nameof(DatabaseBenchmark.Tracked)]);
    }

    /// <summary>Owns process-wide CLI state only while the serialized test collection is active.</summary>
    private sealed class LauncherProcessState : IDisposable
    {
        private const string OptionsVariable = "NESTEDSET_BENCHMARK_OPTIONS";
        private readonly TextWriter _output = Console.Out;
        private readonly TextWriter _error = Console.Error;
        private readonly StringWriter _capturedOutput = new(CultureInfo.InvariantCulture);
        private readonly StringWriter _capturedError = new(CultureInfo.InvariantCulture);
        private readonly string? _options = Environment.GetEnvironmentVariable(OptionsVariable);
        private readonly string? _connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        private readonly string? _docker = Environment.GetEnvironmentVariable("DOCKER_HOST");
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        internal LauncherProcessState()
        {
            Directory = Path.Combine(
                Path.GetTempPath(),
                "nestedset-launcher-tests-" + Guid.NewGuid().ToString("N"));
            FileDirectory.CreateDirectory(Directory);
            Console.SetOut(_capturedOutput);
            Console.SetError(_capturedError);
        }

        internal string Directory { get; }

        internal string Output => _capturedOutput.ToString();

        internal string Error => _capturedError.ToString();

        /// <inheritdoc />
        public void Dispose()
        {
            Console.SetOut(_output);
            Console.SetError(_error);
            Environment.SetEnvironmentVariable(OptionsVariable, _options);
            Environment.SetEnvironmentVariable(ConnectionVariable, _connection);
            Environment.SetEnvironmentVariable("DOCKER_HOST", _docker);
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
            _capturedOutput.Dispose();
            _capturedError.Dispose();
            FileDirectory.Delete(Directory, true);
        }
    }
}
