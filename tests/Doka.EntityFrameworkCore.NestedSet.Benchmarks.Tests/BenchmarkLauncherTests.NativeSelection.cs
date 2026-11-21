namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class BenchmarkLauncherTests
{
    /// <summary>
    /// Listing resolves through native parsed settings before an unreachable Docker provider is acquired.
    /// </summary>
    /// <param name="responseFile">Whether the listing setting is supplied through a response file.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Main_NativeListing_DoesNotAcquireDatabase(
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
            artifacts,
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
    public async Task Main_MissingNativeSelector_FailsBeforeResourceAcquisition()
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
    public async Task HasExplicitSelection_NativeCategorySelectors_CountAsExplicitSelection(
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
    public void Discover_CoreOnlyNativeSelection_RequiresNoDatabase()
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
}
