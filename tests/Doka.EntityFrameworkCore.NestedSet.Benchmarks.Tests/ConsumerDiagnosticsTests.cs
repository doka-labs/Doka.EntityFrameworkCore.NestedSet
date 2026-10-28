namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Checks native untimed diagnostics on actual sample operations through the shared lifecycle.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class ConsumerDiagnosticsTests
{
    /// <summary>Actual sample contexts contribute only measured readers to command diagnostics.</summary>
    /// <param name="groups">Whether group selection or the two-reader signal report is selected.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDiagnosticsUseTheActualConsumerContext(
        bool groups
    )
    {
        // Arrange
        const string variable = "NESTEDSET_BENCHMARK_OPTIONS";
        var previous = Environment.GetEnvironmentVariable(variable);
        var directory = Path.Combine(
            Path.GetTempPath(),
            "nestedset-consumer-diagnostic-"
            + Guid
                .NewGuid()
                .ToString("N"));

        try
        {
            var options = new BenchmarkRunOptions
            {
                NodeCounts = [40],
                Shapes = [BenchmarkShape.Wide],
                Diagnostics = true,
                ArtifactsPath = directory,
            };

            options.ApplyEnvironment();
            await using var environment = await BenchmarkEnvironment.CreateAsync(options, CancellationToken.None);
            var type = groups ? typeof(UserGroupSelectionBenchmarks) : typeof(SignalReportingBenchmarks);
            var configuration = BenchmarkConfiguration.Create(options);
            using var native = BenchmarkConverter.TypeToBenchmarks(type, configuration);
            var selected = native.BenchmarksCases.First(item => item.Descriptor.WorkloadMethod.Name == "Api");
            var exact = ManualConfig
                .Create(configuration)
                .AddFilter(new GlobFilter([FullNameProvider.GetBenchmarkName(selected)]));

            using var discovery = BenchmarkSelection.Discover(exact, diagnostics: true);

            // Act
            await BenchmarkDiagnostics.RunAsync(options, discovery, CancellationToken.None);

            // Assert
            using var report = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(directory, "command-diagnostics.json"),
                    CancellationToken.None));

            var result = Assert.Single(report.RootElement.GetProperty("Results").EnumerateArray());
            Assert.Equal(groups ? 1 : 2, result.GetProperty("Commands").GetInt32());
            Assert.Equal(0, result.GetProperty("HierarchyUpdates").GetInt32());
            Assert.Equal(40, result.GetProperty("Nodes").GetInt32());
            Assert.EndsWith(type.Name + ".Api", result.GetProperty("Benchmark").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
