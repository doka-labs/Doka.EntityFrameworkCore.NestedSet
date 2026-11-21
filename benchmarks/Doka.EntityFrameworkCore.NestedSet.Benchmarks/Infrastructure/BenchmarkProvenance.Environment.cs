namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

public static partial class BenchmarkProvenance
{
    /// <summary>Reads package versions from the running assembly's resolved dependency graph.</summary>
    /// <param name="cancellationToken">Cancellation for dependency metadata IO.</param>
    /// <returns>Resolved package names and exact versions.</returns>
    private static async Task<Dictionary<string, string>> ReadPackagesAsync(
        CancellationToken cancellationToken
    )
    {
        var path = Path.ChangeExtension(typeof(BenchmarkProvenance).Assembly.Location, ".deps.json");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var dependencies = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return dependencies
            .RootElement
            .GetProperty("libraries")
            .EnumerateObject()
            .Where(library => library
                    .Value
                    .GetProperty("type")
                    .GetString()
                == "package")
            .OrderBy(library => library.Name, StringComparer.Ordinal)
            .ToDictionary(
                library => library.Name[..library.Name.LastIndexOf('/')],
                library => library.Name[(library.Name.LastIndexOf('/') + 1)..],
                StringComparer.Ordinal);
    }

    /// <summary>Reads the host CPU description using platform metadata without adding a profiling dependency.</summary>
    /// <param name="cancellationToken">Cancellation for host metadata IO.</param>
    /// <returns>The CPU description or the process architecture when no description is available.</returns>
    private static async Task<string> ReadCpuAsync(
        CancellationToken cancellationToken
    )
    {
        if (OperatingSystem.IsMacOS())
        {
            var cpu = await RunProcessAsync(
                "sysctl",
                ["-n", "machdep.cpu.brand_string"],
                Directory.GetCurrentDirectory(),
                cancellationToken);

            return cpu.ExitCode == 0
                ? cpu.Output.Trim()
                : RuntimeInformation.ProcessArchitecture.ToString();
        }

        if (OperatingSystem.IsLinux())
        {
            var cpu = await File.ReadAllLinesAsync("/proc/cpuinfo", cancellationToken);

            return cpu.FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal))
                ?? cpu.FirstOrDefault(line => line.StartsWith("Hardware", StringComparison.Ordinal))
                ?? RuntimeInformation.ProcessArchitecture.ToString();
        }

        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
            ?? RuntimeInformation.ProcessArchitecture.ToString();
    }
}
