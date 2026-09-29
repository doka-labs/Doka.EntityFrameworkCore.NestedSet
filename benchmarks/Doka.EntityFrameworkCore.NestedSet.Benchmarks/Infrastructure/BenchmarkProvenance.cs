using BenchmarkDotNet.Characteristics;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Persists source, environment and effective measurement settings alongside framework exports.</summary>
public static class BenchmarkProvenance
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Writes a credential-free provenance sidecar, including the actual jobs after a completed run.</summary>
    /// <param name="options">The selected provider and datasets.</param>
    /// <param name="environment">The launcher-owned database metadata, or null for database-free core cases.</param>
    /// <param name="runStartedAtUtc">The independent start timestamp shared by this run's sidecar updates.</param>
    /// <param name="summaries">The framework's actual reports; empty for startup or untimed diagnostics.</param>
    /// <param name="outcome">The run lifecycle outcome, such as started, completed or invalid.</param>
    /// <param name="cancellationToken">Cancellation for source, process and file operations.</param>
    /// <returns>A task that completes when provenance has been written.</returns>
    public static async Task WriteAsync(
        BenchmarkRunOptions options,
        BenchmarkEnvironment? environment,
        DateTimeOffset runStartedAtUtc,
        IReadOnlyList<Summary> summaries,
        string outcome,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(summaries);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(options.ArtifactsPath);

        var directory = Directory.GetCurrentDirectory();
        var path = Path.Combine(options.ArtifactsPath, "provenance.json");
        BenchmarkSourceIdentity source;
        if (outcome != "started"
            && File.Exists(path))
        {
            await using var previous = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            using var initial = await JsonDocument.ParseAsync(previous, cancellationToken: cancellationToken);
            source = initial
                    .RootElement
                    .GetProperty("source")
                    .Deserialize<BenchmarkSourceIdentity>(s_jsonOptions)
                ?? throw new InvalidOperationException("The initial run source identity is missing.");
        }
        else
        {
            source = await ReadSourceIdentityAsync(directory, options.ArtifactsPath, cancellationToken);
        }

        var endSource = outcome == "started"
            ? null
            : await ReadSourceIdentityAsync(directory, options.ArtifactsPath, cancellationToken);

        var sdk = await RunProcessAsync("dotnet", ["--version"], directory, cancellationToken);
        var packages = await ReadPackagesAsync(cancellationToken);
        var cpu = await ReadCpuAsync(cancellationToken);

        var jobs = summaries
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
                    Runtime = report.BenchmarkCase.Job.Environment.Runtime?.Name ?? RuntimeInformation.FrameworkDescription,
                    Toolchain = report.BenchmarkCase.Job.Infrastructure.Toolchain.Name,
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

        var provenance = new
        {
            SchemaVersion = 1,
            Mode = options.Diagnostics ? "diagnostics" : "benchmark",
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
                Sdk = sdk.Output.Trim(),
                OperatingSystem = RuntimeInformation.OSDescription,
                OperatingSystemArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount,
                Cpu = cpu,
                GCSettings.IsServerGC,
                AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            },
            Database = environment is null
                ? null
                : new
                {
                    Engine = environment.Engine.ToString(),
                    environment.CreatedAtUtc,
                    environment.ServerVersion,
                    environment.Image,
                    ContainerCpuLimit = environment.Image is null ? (int?)null : BenchmarkEnvironment.CpuLimit,
                    ContainerMemoryLimitBytes = environment.Image is null
                        ? (long?)null
                        : BenchmarkEnvironment.MemoryLimitBytes,
                    ConnectionPooling = false,
                },
            Packages = packages,
            Jobs = jobs,
        };

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);

        await JsonSerializer.SerializeAsync(stream, provenance, s_jsonOptions, cancellationToken);
    }

    /// <summary>Fingerprints tracked and nonignored working files, including deletions and local changes.</summary>
    /// <param name="directory">A directory within the source repository.</param>
    /// <param name="cancellationToken">Cancellation for Git processes and asynchronous file hashing.</param>
    /// <returns>The commit, dirty state and SHA256 identity of the exact working source.</returns>
    public static Task<BenchmarkSourceIdentity> ReadSourceIdentityAsync(
        string directory,
        CancellationToken cancellationToken = default
    ) => ReadSourceIdentityAsync(directory, null, cancellationToken);

    /// <summary>Excludes this run's generated artifacts while identifying the working source.</summary>
    /// <param name="directory">A directory within the source repository.</param>
    /// <param name="artifactsPath">The generated artifact directory to exclude beneath the repository.</param>
    /// <param name="cancellationToken">Cancellation for source processes and file hashing.</param>
    /// <returns>The committed revision and current working source identity.</returns>
    private static async Task<BenchmarkSourceIdentity> ReadSourceIdentityAsync(
        string directory,
        string? artifactsPath,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var root = await RunProcessAsync("git", ["rev-parse", "--show-toplevel"], directory, cancellationToken);

        if (root.ExitCode != 0)
        {
            throw new InvalidOperationException("Benchmark provenance requires an identifiable source repository.");
        }

        var repository = root.Output.Trim();
        var pathspecs = new List<string>();

        if (artifactsPath is not null
            && Directory.Exists(artifactsPath))
        {
            var artifactsRoot = await RunProcessAsync(
                "git",
                ["rev-parse", "--show-toplevel"],
                artifactsPath,
                cancellationToken);

            if (artifactsRoot.ExitCode == 0
                && string.Equals(repository, artifactsRoot.Output.Trim(), StringComparison.Ordinal))
            {
                // Git resolves parent-directory aliases before reporting the repository-relative artifact path.
                var prefix = await RunProcessAsync(
                    "git",
                    ["rev-parse", "--show-prefix"],
                    artifactsPath,
                    cancellationToken);

                if (prefix.ExitCode != 0)
                {
                    throw new InvalidOperationException("Git could not identify the benchmark artifact directory.");
                }

                var relative = prefix.Output.TrimEnd('\r', '\n', '/');

                if (relative.Length > 0)
                {
                    pathspecs.AddRange(["--", ".", ":(exclude,literal)" + relative]);
                }
            }
        }

        var commit = await RunProcessAsync("git", ["rev-parse", "HEAD"], repository, cancellationToken);
        var status = await RunProcessAsync(
            "git",
            ["status", "--porcelain=v1", "--untracked-files=normal", .. pathspecs],
            repository,
            cancellationToken);

        var inventory = await RunProcessAsync(
            "git",
            [
                "ls-files",
                "--cached",
                "--others",
                "--exclude-standard",
                "-z",
                .. pathspecs
            ],
            repository,
            cancellationToken);

        if (commit.ExitCode != 0
            || status.ExitCode != 0
            || inventory.ExitCode != 0)
        {
            throw new InvalidOperationException("Git could not identify the benchmark source state.");
        }

        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var relativePath in inventory
                     .Output
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            fingerprint.AppendData(Encoding.UTF8.GetBytes(relativePath + "\0"));
            var path = Path.Combine(repository, relativePath);

            if (!File.Exists(path))
            {
                fingerprint.AppendData(Encoding.ASCII.GetBytes("deleted\0"));
                continue;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            fingerprint.AppendData(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        return new BenchmarkSourceIdentity
        {
            Commit = commit.Output.Trim(),
            Dirty = !string.IsNullOrWhiteSpace(status.Output),
            Fingerprint = Convert.ToHexString(fingerprint.GetHashAndReset()),
        };
    }

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

            return cpu.ExitCode == 0 ? cpu.Output.Trim() : RuntimeInformation.ProcessArchitecture.ToString();
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

    /// <summary>Runs metadata commands with separate arguments and cleans up interrupted processes.</summary>
    /// <param name="executable">The metadata executable.</param>
    /// <param name="arguments">The individual process arguments.</param>
    /// <param name="directory">The working source directory.</param>
    /// <param name="cancellationToken">Cancellation for process completion and output reads.</param>
    /// <returns>The process exit status and standard output.</returns>
    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string executable,
        string[] arguments,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process();
        process.StartInfo = start;

        if (!process.Start())
        {
            throw new InvalidOperationException("A benchmark provenance process could not be started.");
        }

        try
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await errors;

            return (process.ExitCode, await output);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            throw;
        }
    }
}

/// <summary>Identifies both a committed revision and its exact working source state.</summary>
public sealed record BenchmarkSourceIdentity
{
    /// <summary>Gets the current Git commit SHA.</summary>
    public required string Commit { get; init; }

    /// <summary>Gets whether Git reports staged, unstaged or untracked changes.</summary>
    public required bool Dirty { get; init; }

    /// <summary>Gets a SHA256 fingerprint of the current tracked and nonignored working files.</summary>
    public required string Fingerprint { get; init; }
}
