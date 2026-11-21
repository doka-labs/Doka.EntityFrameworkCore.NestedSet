using System.Globalization;
using FileDirectory = System.IO.Directory;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>
/// Verifies that native framework selection and artifact arguments reach the actual diagnostic launcher.
/// </summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed partial class BenchmarkLauncherTests
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
    public async Task Main_Diagnostics_HonorNativeFiltersAndArtifacts(
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

        Assert.False(result.TryGetProperty("Milliseconds", out _));

        using var provenance = JsonDocument.Parse(await File.ReadAllTextAsync(provenancePath, CancellationToken.None));
        Assert.Equal(
            "completed",
            provenance
                .RootElement
                .GetProperty("outcome")
                .GetString());

        Assert.Equal(
            "diagnostics",
            provenance
                .RootElement
                .GetProperty("mode")
                .GetString());

        Assert.Equal(
            nativeArtifacts,
            provenance
                .RootElement
                .GetProperty("settings")
                .GetProperty("artifactsPath")
                .GetString());
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
    public async Task Main_InvalidSelectedScenario_FailsBeforeResourceAcquisition(
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

    /// <summary>The smallest valid transfer input executes with the native implicit second tree.</summary>
    [Fact]
    public async Task Main_Diagnostics_HonorImplicitCrossTreeMinimum()
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

        Assert.EndsWith(
            ".CrossTreeMoveBenchmarks.RootUnderOtherTree",
            result
                .GetProperty("Benchmark")
                .GetString());

        Assert.Equal(
            20,
            result
                .GetProperty("Nodes")
                .GetInt32());

        Assert.Equal(
            2,
            result
                .GetProperty("Trees")
                .GetInt32());

        Assert.True(
            result
                .GetProperty("Commands")
                .GetInt32()
            > 0);
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
        private readonly StringWriter _capturedError = new(CultureInfo.InvariantCulture);
        private readonly StringWriter _capturedOutput = new(CultureInfo.InvariantCulture);
        private readonly string? _connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly string? _docker = Environment.GetEnvironmentVariable("DOCKER_HOST");
        private readonly TextWriter _error = Console.Error;
        private readonly string? _options = Environment.GetEnvironmentVariable(OptionsVariable);
        private readonly TextWriter _output = Console.Out;

        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        internal LauncherProcessState()
        {
            Directory = Path.Combine(
                Path.GetTempPath(),
                "nestedset-launcher-tests-"
                + Guid
                    .NewGuid()
                    .ToString("N"));

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
