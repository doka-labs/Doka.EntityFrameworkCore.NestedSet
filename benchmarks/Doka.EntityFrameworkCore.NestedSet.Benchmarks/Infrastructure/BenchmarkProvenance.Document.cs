namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

public static partial class BenchmarkProvenance
{
    /// <summary>Creates the same provenance schema after all source and environment observations complete.</summary>
    private static object CreateDocument(
        BenchmarkRunOptions options,
        BenchmarkEnvironment? environment,
        DateTimeOffset runStartedAtUtc,
        string outcome,
        BenchmarkSourceIdentity source,
        BenchmarkSourceIdentity? endSource,
        string sdk,
        Dictionary<string, string> packages,
        string cpu,
        object[] jobs
    ) => new
        {
            SchemaVersion = 1,
            Mode = options.Diagnostics
                ? "diagnostics"
                : "benchmark",
            Outcome = outcome,
            CreatedAtUtc = runStartedAtUtc,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Source = source,
            EndSource = endSource,
            SourcesChangedDuringRun = endSource is not null && endSource != source,
            Settings = new
            {
                Engine = options.Engine.ToString(),
                options.NodeCounts,
                Shapes = options
                    .Shapes
                    .Select(shape => shape.ToString())
                    .ToArray(),
                options.TreeCounts,
                options.TrackedCounts,
                options.ArtifactsPath,
            },
            Host = new
            {
                Runtime = RuntimeInformation.FrameworkDescription,
                Sdk = sdk,
                OperatingSystem = RuntimeInformation.OSDescription,
                OperatingSystemArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount,
                Cpu = cpu,
                GCSettings.IsServerGC,
                AvailableMemoryBytes = GC.GetGCMemoryInfo()
                    .TotalAvailableMemoryBytes,
            },
            Database = environment is null
                ? null
                : new
                {
                    Engine = environment.Engine.ToString(),
                    environment.CreatedAtUtc,
                    environment.ServerVersion,
                    environment.Image,
                    ContainerCpuLimit = environment.Image is null
                        ? (int?)null
                        : BenchmarkEnvironment.CpuLimit,
                    ContainerMemoryLimitBytes = environment.Image is null
                        ? (long?)null
                        : BenchmarkEnvironment.MemoryLimitBytes,
                    ConnectionPooling = false,
                },
            Packages = packages,
            Jobs = jobs,
        };
}
