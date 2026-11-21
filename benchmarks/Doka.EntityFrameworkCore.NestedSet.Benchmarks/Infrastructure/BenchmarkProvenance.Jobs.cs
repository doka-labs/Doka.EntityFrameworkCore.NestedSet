using BenchmarkDotNet.Characteristics;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

public static partial class BenchmarkProvenance
{
    /// <summary>Records configured job values alongside invocation counts observed in workload measurements.</summary>
    /// <param name="characteristics">The actual job run, accuracy or GC configuration.</param>
    /// <returns>Characteristic names and invariant formatted values without inherited process credentials.</returns>
    private static Dictionary<string, string> DescribeCharacteristics(
        CharacteristicObject characteristics
    ) => characteristics
        .GetCharacteristicsWithValues()
        .ToDictionary(
            characteristic => characteristic.Id,
            characteristic => Convert.ToString(
                    characteristics.ResolveValue(characteristic, string.Empty),
                    CultureInfo.InvariantCulture)
                ?? string.Empty,
            StringComparer.Ordinal);

    /// <summary>Projects framework reports without changing configured jobs or inventing effective defaults.</summary>
    private static object[] DescribeJobs(
        IReadOnlyList<Summary> summaries
    ) => summaries
        .SelectMany(summary => summary.Reports)
        .Select(report => new
        {
            Benchmark = report.BenchmarkCase.Descriptor.Type.FullName,
            Method = report.BenchmarkCase.Descriptor.WorkloadMethod.Name,
            Parameters = report.BenchmarkCase.Parameters.PrintInfo,
            report.Success,
            Job = new
            {
                report.BenchmarkCase.Job.Id,
                Runtime = report.BenchmarkCase.Job.Environment.Runtime?.Name
                    ?? RuntimeInformation.FrameworkDescription,

                // WHY: BDN resolves an unset toolchain internally. Null preserves that configured/default
                // distinction without inventing an effective toolchain that its public API does not expose.
                Toolchain = report.BenchmarkCase.Job.Infrastructure.Toolchain?.Name,
                BuildConfiguration = report.BenchmarkCase.Job.Infrastructure.ResolveValue(
                    InfrastructureMode.BuildConfigurationCharacteristic,
                    "Release"),
                Run = DescribeCharacteristics(report.BenchmarkCase.Job.Run),
                Accuracy = DescribeCharacteristics(report.BenchmarkCase.Job.Accuracy),
                GarbageCollection = DescribeCharacteristics(report.BenchmarkCase.Job.Environment.Gc),
                ActualWorkloadMeasurements = report
                    .AllMeasurements
                    .Where(measurement => measurement.IterationMode == IterationMode.Workload)
                    .Select(measurement => new
                    {
                        measurement.LaunchIndex,
                        Stage = measurement.IterationStage.ToString(),
                        measurement.IterationIndex,
                        measurement.Operations,
                    }),
            },
        })
        .ToArray();
}
