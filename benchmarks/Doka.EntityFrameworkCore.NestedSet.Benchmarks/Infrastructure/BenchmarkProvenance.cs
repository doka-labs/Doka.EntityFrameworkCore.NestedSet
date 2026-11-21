namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Persists source, environment and effective measurement settings alongside framework exports.</summary>
public static partial class BenchmarkProvenance
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
        CancellationToken cancellationToken
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

        var jobs = DescribeJobs(summaries);

        var provenance = CreateDocument(
            options,
            environment,
            runStartedAtUtc,
            outcome,
            source,
            endSource,
            sdk.Output.Trim(),
            packages,
            cpu,
            jobs);

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);

        await JsonSerializer.SerializeAsync(stream, provenance, s_jsonOptions, cancellationToken);
    }
}
