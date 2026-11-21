namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

public static partial class BenchmarkProvenance
{
    /// <summary>Fingerprints tracked and nonignored working files, including deletions and local changes.</summary>
    /// <param name="directory">A directory within the source repository.</param>
    /// <param name="cancellationToken">Cancellation for Git processes and asynchronous file hashing.</param>
    /// <returns>The commit, dirty state and SHA256 identity of the exact working source.</returns>
    public static Task<BenchmarkSourceIdentity> ReadSourceIdentityAsync(
        string directory,
        CancellationToken cancellationToken
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
                .. pathspecs,
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
                fingerprint.AppendData("deleted\0"u8.ToArray());
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

        using var process = new Process { StartInfo = start };

        return await RunProcessAsync(process, cancellationToken);
    }

    /// <summary>Completes an owned metadata child before returning or propagating its original failure.</summary>
    /// <param name="process">The unstarted caller-owned wrapper, disposed by its caller after completion.</param>
    /// <param name="cancellationToken">
    /// Cancels forward waiting while termination and stream drainage remain protected.
    /// </param>
    /// <returns>The exit status and captured standard output after complete stream observation.</returns>
    internal static async Task<(int ExitCode, string Output)> RunProcessAsync(
        Process process,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(process);
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: Start failure owns no child. Once launched, every path below must terminate or observe its exit.
        if (!process.Start())
        {
            throw new InvalidOperationException("A benchmark provenance process could not be started.");
        }

        Task<string>? outputTask = null;
        Task<string>? errorTask = null;

        try
        {
            // WHY: Forward cancellation stops waiting, then kills the child. Uncanceled drains retain EOF and
            // ensure both streams remain observable during protected cleanup instead of abandoning reader tasks.
            outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask.WaitAsync(cancellationToken);
            await errorTask.WaitAsync(cancellationToken);

            return (process.ExitCode, output);
        }
        catch (Exception failure)
        {
            var cleanupFailures = new List<Exception>();

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // WHY: A natural child exit can race the preceding observation before Kill executes.
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures.Add(cleanupFailure);
            }

            try
            {
                // WHY: Cleanup cannot release the Process wrapper before the owned child has been reaped.
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures.Add(cleanupFailure);
            }

            // WHY: These two existing tasks must both be observed even when capture failed between acquisitions.
            Task<string>?[] reads = [outputTask, errorTask];

            foreach (var read in reads)
            {
                if (read is null)
                {
                    continue;
                }

                try
                {
                    await read;
                }
                catch (Exception cleanupFailure)
                {
                    if (!ReferenceEquals(cleanupFailure, failure))
                    {
                        cleanupFailures.Add(cleanupFailure);
                    }
                }
            }

            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, failure);

                throw new AggregateException(
                    "A benchmark provenance command failed and child cleanup also failed.",
                    cleanupFailures);
            }

            throw;
        }
    }
}
