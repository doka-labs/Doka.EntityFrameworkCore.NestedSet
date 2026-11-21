namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Retains provider identities and no-I/O semantics without statistical benchmark execution.</summary>
public sealed class ProviderMeasurementContractTests
{
    /// <summary>Every real selected adapter remains owned after ordinary or collected setup.</summary>
    /// <param name="engine">The actual provider callback profile.</param>
    /// <param name="collected">Whether setup explicitly collects weak runtime state.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    [InlineData("Sqlite", true)]
    public void SelectedProvider_Workload_RetainsAdapterOwnership(
        string engine,
        bool collected
    )
    {
        // Arrange
        var benchmark = new SelectedProviderResolutionBenchmarks
        {
            Engine = engine,
            CollectBeforeMeasurement = collected,
        };

        benchmark.Setup();
        try
        {
            // Act
            var adapter = benchmark.Resolve();

            // Assert
            Assert.NotNull(adapter);
            benchmark.Validate();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>An unknown provider profile rejects before allocating an owned service graph.</summary>
    [Fact]
    public void SelectedProvider_UnknownEngine_RejectsSetup()
    {
        // Arrange
        var benchmark = new SelectedProviderResolutionBenchmarks { Engine = "unknown" };

        // Act
        var error = Record.Exception(benchmark.Setup);

        // Assert
        Assert.Equal("engine", Assert.IsType<ArgumentOutOfRangeException>(error).ParamName);
    }

    /// <summary>Both Doka engine profiles retain their actual public marker identities.</summary>
    /// <param name="engine">The configured Doka engine profile.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void ProviderMarkers_Workload_RetainsExactIdentities(
        string engine
    )
    {
        // Arrange
        var benchmark = new ProviderMarkerIdentityBenchmarks { Engine = engine };
        benchmark.Setup();
        try
        {
            // Act
            var source = benchmark.CompletedSource();
            var join = benchmark.OrderedJoin();

            // Assert
            Assert.NotNull(source);
            Assert.NotNull(join);
            benchmark.Validate();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>The diagnostic workload preserves one initial event across repeated cached checks.</summary>
    [Fact]
    public void AncestorFallback_Workload_RetainsOncePerContextEvent()
    {
        // Arrange
        var benchmark = new AncestorFallbackCheckBenchmarks();
        benchmark.Setup();
        try
        {
            // Act
            benchmark.Check();

            // Assert
            benchmark.Validate();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
