using System.Diagnostics;

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
        await using var repository = await TemporaryRepository.CreateAsync();
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
        Assert.Equal(initialSource, metadata.GetProperty("source").GetRawText());
        Assert.Equal(JsonValueKind.Null, initial.RootElement.GetProperty("endSource").ValueKind);
        Assert.False(initial.RootElement.GetProperty("sourcesChangedDuringRun").GetBoolean());
        Assert.Equal(changeSource, metadata.GetProperty("sourcesChangedDuringRun").GetBoolean());
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
        private readonly string _directory = Directory.GetCurrentDirectory();
        private readonly string? _options = Environment.GetEnvironmentVariable(OptionsVariable);
        private readonly string? _connection = Environment.GetEnvironmentVariable(ConnectionVariable);

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

        internal static async Task<TemporaryRepository> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "nestedset-source-" + Guid.NewGuid().ToString("N"));

            System.IO.Directory.CreateDirectory(directory);
            var repository = new TemporaryRepository(directory);
            await BenchmarkLifecycle.InitializeAsync(
                repository,
                async () =>
                {
                    await RunGitAsync(directory, ["init", "--quiet"]);
                    await File.WriteAllTextAsync(
                        repository.SourceFile,
                        "class OriginalSource { }\n",
                        CancellationToken.None);
                    await RunGitAsync(directory, ["add", "source.cs"]);
                    await RunGitAsync(directory, ["commit", "--quiet", "--message", "source fixture"]);
                });

            return repository;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            System.IO.Directory.Delete(Directory, true);

            return ValueTask.CompletedTask;
        }

        /// <summary>Runs Git only against the owned fixture with hooks, signing and global identity disabled.</summary>
        private static async Task RunGitAsync(
            string directory,
            string[] arguments
        )
        {
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

            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None);
            await output;
            var failure = await error;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Source fixture Git failed: " + failure);
            }
        }
    }
}
