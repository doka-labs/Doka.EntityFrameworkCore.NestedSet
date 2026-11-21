using BenchmarkRunMode = BenchmarkDotNet.Jobs.RunMode;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Provides the shared exporters and allocation diagnostics for observational benchmarks.</summary>
public static class BenchmarkConfiguration
{
    /// <summary>Creates an out-of-process configuration without performance thresholds.</summary>
    /// <param name="options">The selected artifact directory.</param>
    /// <returns>A configuration whose core benchmarks use standard throughput measurement.</returns>
    public static IConfig Create(
        BenchmarkRunOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        return ManualConfig
            .Create(DefaultConfig.Instance)
            .AddExporter(JsonExporter.Full, CsvMeasurementsExporter.Default)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddValidator(new ObservationValidator())
            .WithArtifactsPath(options.ArtifactsPath)
            .WithOption(ConfigOptions.StopOnFirstError, true);
    }

    /// <summary>Rejects execution settings that invalidate isolation or one-operation database observations.</summary>
    private sealed class ObservationValidator : IValidator
    {
        /// <inheritdoc />
        public bool TreatsWarningsAsErrors => true;

        /// <inheritdoc />
        public IEnumerable<ValidationError> Validate(
            ValidationParameters validationParameters
        )
        {
            // WHY: BDN supplies a stable indexable validation snapshot; preserve its original diagnostic order.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < validationParameters.Benchmarks.Count; index++)
            {
                var benchmark = validationParameters.Benchmarks[index];
                var job = benchmark.Job;

                if (job.Infrastructure.HasValue(InfrastructureMode.ToolchainCharacteristic)
                    && job.Infrastructure.Toolchain.IsInProcess)
                {
                    yield return new ValidationError(
                        true,
                        "Observational benchmarks require an isolated child process; remove --inProcess.",
                        benchmark);
                }

                if (!string.Equals(
                    job.Infrastructure.ResolveValue(InfrastructureMode.BuildConfigurationCharacteristic, "Release"),
                    "Release",
                    StringComparison.Ordinal))
                {
                    yield return new ValidationError(true, "Benchmark measurements require Release builds.", benchmark);
                }

                if (benchmark.Descriptor.IterationSetupMethod is not null
                    && (job.Run.ResolveValue(BenchmarkRunMode.InvocationCountCharacteristic, 1L) != 1
                        || job.Run.ResolveValue(BenchmarkRunMode.UnrollFactorCharacteristic, 1) != 1))
                {
                    yield return new ValidationError(
                        true,
                        "Database scenarios require invocation count and unroll factor 1 for each restored state.",
                        benchmark);
                }
            }
        }
    }
}
