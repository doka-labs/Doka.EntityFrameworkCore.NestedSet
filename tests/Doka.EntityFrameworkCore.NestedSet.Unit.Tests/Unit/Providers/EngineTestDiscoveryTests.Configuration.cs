using Xunit.Runner.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class EngineTestDiscoveryTests
{
    /// <summary>Native discovery retains ordinary rows when another data source excludes its engine.</summary>
    [Theory]
    [InlineData("Sqlite", nameof(DiscoveryProbe.MixedInline), 1)]
    [InlineData("MySql", nameof(DiscoveryProbe.MixedInline), 2)]
    [InlineData("Sqlite", nameof(DiscoveryProbe.MixedMember), 1)]
    [InlineData("MySql", nameof(DiscoveryProbe.MixedMember), 2)]
    public async Task ConfiguredDiscoveryRetainsMixedSources(
        string engine,
        string methodName,
        int expectedCount
    )
    {
        // Arrange
        var method = CreateTestMethod(engine, methodName);
        var configuration = LoadRunnerConfiguration();
        var options = TestFrameworkOptions.ForDiscovery(configuration);
        var attribute = Assert.Single(method.FactAttributes);

        // Act
        var cases = await new TheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        Assert.True(configuration.PreEnumerateTheories);
        Assert.Equal(expectedCount, cases.Count);

        var concreteCases = cases
            .Select(Assert.IsType<XunitTestCase>)
            .ToArray();

        Assert.All(concreteCases, testCase => Assert.Null(testCase.SkipReason));
        Assert.Contains(concreteCases, testCase => Equals(Assert.Single(testCase.TestMethodArguments), 43));
    }

    /// <summary>Configured pre-enumeration never hides empty or accidentally over-filtered theories.</summary>
    [Theory]
    [InlineData(nameof(DiscoveryProbe.Empty))]
    [InlineData(nameof(DiscoveryProbe.OverFiltered))]
    public async Task ConfiguredDiscoveryRejectsMissingData(
        string methodName
    )
    {
        // Arrange
        var method = CreateTestMethod("Sqlite", methodName);
        var options = TestFrameworkOptions.ForDiscovery(LoadRunnerConfiguration());
        var attribute = Assert.IsType<EngineTheoryAttribute>(Assert.Single(method.FactAttributes));

        // Act
        var cases = await new EngineTheoryDiscoverer().Discover(options, method, attribute);

        // Assert
        var failure = Assert.IsType<ExecutionErrorTestCase>(Assert.Single(cases));
        Assert.False(attribute.SkipTestWithoutData);
        Assert.Null(failure.SkipReason);
        Assert.Contains("No data found", failure.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>Uses the native runner's public loader against the shared configuration copied by MSBuild.</summary>
    private static TestAssemblyConfiguration LoadRunnerConfiguration()
    {
        var configuration = new TestAssemblyConfiguration();
        var warnings = new List<string>();
        var path = Path.Combine(AppContext.BaseDirectory, "xunit.runner.json");

        var loaded = ConfigReader_Json.Load(
            configuration,
            typeof(EngineTestDiscoveryTests).Assembly.Location,
            path,
            warnings);

        Assert.True(loaded);
        Assert.Empty(warnings);

        return configuration;
    }

    public abstract partial class DiscoveryProbe
    {
        /// <summary>Combines a genuinely unavailable variant with an ordinary row that must still execute.</summary>
        [Theory]
        [EngineInlineData(17, ExcludedEngines = ["Sqlite"], Reason = "The synthetic variant requires a server engine.")]
        [InlineData(43)]
        public void MixedInline(
            int value
        ) => RequireMetadataOnly(value);

        /// <summary>Retains an ordinary row when a later member source yields no applicable variants.</summary>
        [Theory]
        [EngineInlineData(43)]
        [EngineMemberData(nameof(OverFilteredRows))]
        public void MixedMember(
            int value
        ) => RequireMetadataOnly(value);
    }
}
