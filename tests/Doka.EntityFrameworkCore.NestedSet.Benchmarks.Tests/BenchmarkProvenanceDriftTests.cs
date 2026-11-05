using System.Diagnostics;
using FileDirectory = System.IO.Directory;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies exact source attribution across a run using an isolated source-repository fixture.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class BenchmarkProvenanceDriftTests
{
    /// <summary>Final provenance retains the started source and explicitly reports any changed end snapshot.</summary>
    /// <param name="changeSource">Whether a working source file changes after the run begins.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalProvenanceRetainsInitialSourceAndReportsDrift(
        bool changeSource
    )
    {
        // Arrange
        await using var repository = await TemporaryRepository.CreateAsync(TestContext.Current.CancellationToken);
        using var processState = new ProcessState(repository.Directory);
        var options = new BenchmarkRunOptions
        {
            Engine = BenchmarkEngine.SqliteMemory,
            ArtifactsPath = Path.Combine(repository.Directory, "results"),
        };

        options.ApplyEnvironment();
        await using var environment = await BenchmarkEnvironment.CreateAsync(options, CancellationToken.None);
        await BenchmarkProvenance.WriteAsync(
            options,
            environment,
            environment.CreatedAtUtc,
            [],
            "started",
            CancellationToken.None);

        var path = Path.Combine(options.ArtifactsPath, "provenance.json");
        using var initial = JsonDocument.Parse(await File.ReadAllTextAsync(path, CancellationToken.None));
        var initialSource = initial
            .RootElement
            .GetProperty("source")
            .GetRawText();

        if (changeSource)
        {
            await File.WriteAllTextAsync(repository.SourceFile, "class ChangedSource { }\n", CancellationToken.None);
        }

        // Act
        await BenchmarkProvenance.WriteAsync(
            options,
            environment,
            environment.CreatedAtUtc,
            [],
            "completed",
            CancellationToken.None);

        // Assert
        using var completed = JsonDocument.Parse(await File.ReadAllTextAsync(path, CancellationToken.None));
        var metadata = completed.RootElement;
        Assert.Equal(
            initialSource,
            metadata
                .GetProperty("source")
                .GetRawText());

        Assert.Equal(
            JsonValueKind.Null,
            initial.RootElement.GetProperty("endSource")
                .ValueKind);

        Assert.False(
            initial
                .RootElement
                .GetProperty("sourcesChangedDuringRun")
                .GetBoolean());

        Assert.Equal(
            changeSource,
            metadata
                .GetProperty("sourcesChangedDuringRun")
                .GetBoolean());

        Assert.Equal(
            changeSource,
            metadata
                .GetProperty("source")
                .GetProperty("fingerprint")
                .GetString()
            != metadata
                .GetProperty("endSource")
                .GetProperty("fingerprint")
                .GetString());

        Assert.Equal(
            initial
                .RootElement
                .GetProperty("source")
                .GetProperty("commit")
                .GetString(),
            metadata
                .GetProperty("endSource")
                .GetProperty("commit")
                .GetString());

        Assert.False(
            initial
                .RootElement
                .GetProperty("source")
                .GetProperty("dirty")
                .GetBoolean());

        Assert.Equal(
            changeSource,
            metadata
                .GetProperty("endSource")
                .GetProperty("dirty")
                .GetBoolean());
    }

    /// <summary>Exclusively owns process environment and current directory inside the serialized collection.</summary>
    private sealed class ProcessState : IDisposable
    {
        private const string OptionsVariable = "NESTEDSET_BENCHMARK_OPTIONS";
        private const string ConnectionVariable = "NESTEDSET_BENCHMARK_CONNECTION_STRING";
        private readonly string? _connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        private readonly string _directory = Directory.GetCurrentDirectory();
        private readonly string? _options = Environment.GetEnvironmentVariable(OptionsVariable);

        internal ProcessState(
            string directory
        )
        {
            Directory.SetCurrentDirectory(directory);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Directory.SetCurrentDirectory(_directory);
            Environment.SetEnvironmentVariable(OptionsVariable, _options);
            Environment.SetEnvironmentVariable(ConnectionVariable, _connection);
        }
    }

    /// <summary>
    /// Creates a minimal Git source fixture without touching the product repository or user Git settings.
    /// </summary>
    private sealed class TemporaryRepository : IAsyncDisposable
    {
        private TemporaryRepository(
            string directory
        )
        {
            Directory = directory;
        }

        internal string Directory { get; }

        internal string SourceFile => Path.Combine(Directory, "source.cs");

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            FileDirectory.Delete(Directory, true);

            return ValueTask.CompletedTask;
        }

        /// <summary>Creates an independently owned benchmark fixture.</summary>
        /// <param name="cancellationToken">The caller's cancellation token for the operation.</param>
        internal static async Task<TemporaryRepository> CreateAsync(
            CancellationToken cancellationToken
        )
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "nestedset-source-"
                + Guid
                    .NewGuid()
                    .ToString("N"));

            FileDirectory.CreateDirectory(directory);
            var repository = new TemporaryRepository(directory);
            await BenchmarkLifecycle.InitializeAsync(
                repository,
                async token =>
                {
                    await RunGitAsync(directory, ["init", "--quiet"], token);
                    await File.WriteAllTextAsync(
                        repository.SourceFile,
                        "class OriginalSource { }\n",
                        token);

                    await RunGitAsync(directory, ["add", "source.cs"], token);
                    await RunGitAsync(directory, ["commit", "--quiet", "--message", "source fixture"], token);
                },
                cancellationToken);

            return repository;
        }

        /// <summary>Runs Git only against the owned fixture with hooks, signing and global identity disabled.</summary>
        /// <param name="directory">The owned repository working directory.</param>
        /// <param name="arguments">The individual Git command arguments.</param>
        /// <param name="cancellationToken">Cancellation for the operation's preparation or verification work.</param>
        private static async Task RunGitAsync(
            string directory,
            string[] arguments,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            string[] configuration =
            [
                "-c",
                "core.hooksPath=" + Path.Combine(directory, "disabled-hooks"),
                "-c",
                "commit.gpgsign=false",
                "-c",
                "user.name=Benchmark fixture",
                "-c",
                "user.email=benchmark-fixture@example.invalid",
                "-c",
                "init.defaultBranch=main",
            ];

            foreach (var argument in configuration.Concat(arguments))
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("The source fixture could not start Git.");

            // WHY: Stream drains must also finish after cancellation kills the child; only the live wait is cancelable.
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await output;
                var failure = await error;

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Source fixture Git failed: " + failure);
                }
            }
            catch (Exception original)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException) when (process.HasExited)
                        {
                            // WHY: A natural exit can race the cancellation cleanup's preceding HasExited check.
                        }
                    }

                    // WHY: Caller cancellation cannot release the owned Process before exit and stream observation.
                    await process.WaitForExitAsync(CancellationToken.None);
                    await Task.WhenAll(output, error);
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(original, cleanup);
                }

                throw;
            }
        }
    }
}
