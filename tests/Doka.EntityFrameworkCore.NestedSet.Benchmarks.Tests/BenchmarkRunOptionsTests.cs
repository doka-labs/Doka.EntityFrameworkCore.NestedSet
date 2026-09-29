namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Serializes tests that temporarily publish the launcher's inherited child-process environment.</summary>
/// <remarks>
/// WHY: The environment belongs to the entire process and must not overlap independent database tests.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BenchmarkEnvironmentOwnership
{
    /// <summary>Identifies the collection that exclusively owns inherited benchmark settings.</summary>
    public const string Name = "Benchmark environment ownership";
}

/// <summary>Verifies explicit datasets, child-process settings and invalid command-line input.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class BenchmarkRunOptionsTests
{
    private const string OptionsVariable = "NESTEDSET_BENCHMARK_OPTIONS";

    /// <summary>Gets malformed input arrays without encoding nested arrays in attribute arguments.</summary>
    public static TheoryData<string[]> InvalidArgumentCases
    {
        get
        {
            var data = new TheoryData<string[]>();
            data.Add(["--engine"]);
            data.Add(["--engine=0"]);
            data.Add(["--engine=MissingProvider"]);
            data.Add(["--nodes=9"]);
            data.Add(["--nodes=10,"]);
            data.Add(["--nodes=-10"]);
            data.Add(["--shape=99"]);
            data.Add(["--trees=0"]);
            data.Add(["--tracked=-1"]);
            data.Add(["--artifacts=relative"]);
            data.Add(["--artifacts="]);
            data.Add(["--diagnostics=true"]);

            return data;
        }
    }

    /// <summary>The harness consumes its options and preserves the framework's filter and job arguments.</summary>
    [Fact]
    public void ParsePreservesFrameworkArgumentsAndExplicitScenarios()
    {
        // Arrange
        var artifacts = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nestedset-options"));
        string[] arguments =
        [
            "--engine=SqliteFile",
            "--nodes",
            "100,1000,100",
            "--shape=Wide,Deep,Balanced",
            "--trees=1,2",
            "--tracked=0,100",
            "--artifacts",
            artifacts,
            "--diagnostics",
            "--filter",
            "*Insert*",
            "--job",
            "short",
        ];

        // Act
        var options = BenchmarkRunOptions.Parse(arguments);

        // Assert
        Assert.Equal(BenchmarkEngine.SqliteFile, options.Engine);
        Assert.Equal([100, 1000], options.NodeCounts);
        Assert.Equal([BenchmarkShape.Wide, BenchmarkShape.Deep, BenchmarkShape.Balanced], options.Shapes);
        Assert.Equal([1, 2], options.TreeCounts);
        Assert.Equal([0, 100], options.TrackedCounts);
        Assert.Equal(artifacts, options.ArtifactsPath);
        Assert.True(options.Diagnostics);
        Assert.Equal(["--filter", "*Insert*", "--job", "short"], options.Arguments);
    }

    /// <summary>Malformed harness input fails before provider provisioning or benchmark generation.</summary>
    /// <param name="arguments">The invalid command line.</param>
    [Theory]
    [MemberData(nameof(InvalidArgumentCases))]
    public void InvalidArgumentsAreRejected(
        string[] arguments
    )
    {
        // Arrange
        var input = arguments.ToArray();

        // Act
        var error = Record.Exception(() => BenchmarkRunOptions.Parse(input));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>The child-process consumer reconstructs all settings from inherited serialized state.</summary>
    [Fact]
    public void PublishedOptionsAreRehydratedFromEnvironment()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable(OptionsVariable);
        var original = new BenchmarkRunOptions
        {
            Engine = BenchmarkEngine.PostgreSql,
            NodeCounts = [100, 100000],
            Shapes = [BenchmarkShape.Deep, BenchmarkShape.Wide],
            TreeCounts = [2, 4],
            TrackedCounts = [0, 100],
            ArtifactsPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nestedset-child-options")),
            Diagnostics = true,
            Arguments = ["--filter", "*Move*"],
        };

        original.ApplyEnvironment();

        try
        {
            // Act
            var inherited = BenchmarkRunOptions.Current;

            // Assert
            Assert.NotSame(original, inherited);
            Assert.Equal(original.Engine, inherited.Engine);
            Assert.Equal(original.NodeCounts, inherited.NodeCounts);
            Assert.Equal(original.Shapes, inherited.Shapes);
            Assert.Equal(original.TreeCounts, inherited.TreeCounts);
            Assert.Equal(original.TrackedCounts, inherited.TrackedCounts);
            Assert.Equal(original.ArtifactsPath, inherited.ArtifactsPath);
            Assert.Equal(original.Diagnostics, inherited.Diagnostics);
            Assert.Equal(original.Arguments, inherited.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OptionsVariable, previous);
        }
    }

    /// <summary>Invalid inherited data fails even when it did not pass through the launcher parser.</summary>
    [Fact]
    public void ChildProcessRejectsInvalidInheritedSettings()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable(OptionsVariable);
        var invalid = new BenchmarkRunOptions { NodeCounts = [0] };
        Environment.SetEnvironmentVariable(OptionsVariable, JsonSerializer.Serialize(invalid));

        try
        {
            // Act
            var error = Record.Exception(() => BenchmarkRunOptions.Current);

            // Assert
            Assert.IsType<ArgumentException>(error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OptionsVariable, previous);
        }
    }
}
