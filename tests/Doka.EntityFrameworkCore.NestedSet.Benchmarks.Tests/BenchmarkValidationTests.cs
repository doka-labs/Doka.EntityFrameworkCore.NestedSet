using System.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Separates workload correctness from the Release requirement for actual measurements.</summary>
public sealed class BenchmarkValidationTests
{
    /// <summary>The real measurement configuration rejects unoptimized assemblies and accepts optimized ones.</summary>
    [Fact]
    public void MeasurementValidationRequiresOptimizedAssemblies()
    {
        // Arrange
        using var discovery = BenchmarkConverter.TypeToBenchmarks(
            typeof(CompositionConstructionBenchmarks),
            BenchmarkConfiguration.Create(new BenchmarkRunOptions()));

        var validation = new ValidationParameters(discovery.BenchmarksCases, discovery.Config);
        var unoptimized = typeof(CompositionConstructionBenchmarks).Assembly
            .GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true;

        // Act
        var validators = discovery.Config.GetValidators().OfType<JitOptimizationsValidator>().ToArray();
        var errors = validators.SelectMany(validator => validator.Validate(validation))
            .Where(error => error.IsCritical)
            .ToArray();

        // Assert
        var optimizer = Assert.Single(validators);
        Assert.True(optimizer.TreatsWarningsAsErrors);
        Assert.Equal(unoptimized, errors.Length > 0);
        Assert.False(discovery.Config.Options.HasFlag(ConfigOptions.DisableOptimizationsValidator));
    }

    /// <summary>Functional validation still rejects jobs that reuse mutated state within an iteration.</summary>
    [Fact]
    public void StructuralValidationRetainsMutationIsolationErrors()
    {
        // Arrange
        var configuration = ManualConfig.Create(BenchmarkConfiguration.Create(new BenchmarkRunOptions()))
            .AddJob(Job.Default.WithInvocationCount(2).WithUnrollFactor(2));

        using var discovery = BenchmarkConverter.TypeToBenchmarks(typeof(InsertBenchmarks), configuration);

        // Act
        var errors = BenchmarkValidationTestSupport.StructuralErrors(discovery);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, error => error.Message.Contains(
            "invocation count and unroll factor 1", StringComparison.Ordinal));
    }
}
