using FilePath = System.IO.Path;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies stored provider provenance and disposal of launcher-owned embedded resources.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class BenchmarkProvenanceTests
{
    private const string OptionsVariable = "NESTEDSET_BENCHMARK_OPTIONS";
    private const string ConnectionVariable = "NESTEDSET_BENCHMARK_CONNECTION_STRING";

    /// <summary>
    /// The exported sidecar includes source, settings, host and engine identity without a connection string.
    /// </summary>
    [Fact]
    public async Task ProvenanceContainsReproducibleCredentialFreeMetadata()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        using var settings = new InheritedSettings();
        var options = new BenchmarkRunOptions
        {
            ArtifactsPath = directory.Path,
            Engine = BenchmarkEngine.SqliteMemory,
            NodeCounts = [100, 1000],
            Shapes = [BenchmarkShape.Deep, BenchmarkShape.Balanced],
            TreeCounts = [1, 2],
            TrackedCounts = [0, 100],
        };

        options.ApplyEnvironment();
        await using var environment = await BenchmarkEnvironment.CreateAsync(options, CancellationToken.None);

        // Act
        await BenchmarkProvenance.WriteAsync(
            options,
            environment,
            environment.CreatedAtUtc,
            [],
            "completed",
            CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(
            Path.Combine(directory.Path, "provenance.json"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(content);
        var metadata = document.RootElement;
        Assert.Equal(1, metadata.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("benchmark", metadata.GetProperty("mode").GetString());
        Assert.Equal("completed", metadata.GetProperty("outcome").GetString());
        Assert.Equal(
            40,
            metadata
                .GetProperty("source")
                .GetProperty("commit")
                .GetString()!.Length);
        Assert.Equal(
            64,
            metadata
                .GetProperty("source")
                .GetProperty("fingerprint")
                .GetString()!.Length);
        Assert.Equal(
            "SqliteMemory",
            metadata
                .GetProperty("settings")
                .GetProperty("engine")
                .GetString());
        Assert.Equal(
            [100, 1000],
            metadata
                .GetProperty("settings")
                .GetProperty("nodeCounts")
                .EnumerateArray()
                .Select(value => value.GetInt32()));
        Assert.Equal(
            ["Deep", "Balanced"],
            metadata
                .GetProperty("settings")
                .GetProperty("shapes")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            environment.ServerVersion,
            metadata
                .GetProperty("database")
                .GetProperty("serverVersion")
                .GetString());
        Assert.False(
            metadata
                .GetProperty("database")
                .GetProperty("connectionPooling")
                .GetBoolean());
        Assert.False(
            string.IsNullOrWhiteSpace(
                metadata
                    .GetProperty("host")
                    .GetProperty("sdk")
                    .GetString()));
        Assert.False(
            string.IsNullOrWhiteSpace(
                metadata
                    .GetProperty("host")
                    .GetProperty("cpu")
                    .GetString()));
        Assert.Equal(
            "0.15.8",
            metadata
                .GetProperty("packages")
                .GetProperty("BenchmarkDotNet")
                .GetString());
        Assert.Empty(metadata.GetProperty("jobs").EnumerateArray());
        Assert.DoesNotContain("connectionString", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source=", content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A core observation records its run identity without claiming database acquisition.</summary>
    [Fact]
    public async Task CoreProvenanceHasNoDatabaseMetadata()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        using var settings = new InheritedSettings();
        const string previous = "core-observation-must-not-publish-a-connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previous);
        var options = new BenchmarkRunOptions
        {
            Engine = BenchmarkEngine.MySql,
            ArtifactsPath = directory.Path,
        };

        var started = new DateTimeOffset(2026, 9, 29, 1, 2, 3, TimeSpan.Zero);

        // Act
        await BenchmarkProvenance.WriteAsync(options, null, started, [], "completed", CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(
            Path.Combine(directory.Path, "provenance.json"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(content);
        var metadata = document.RootElement;

        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("database").ValueKind);
        Assert.Equal(started, metadata.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal("completed", metadata.GetProperty("outcome").GetString());
        Assert.Equal("MySql", metadata.GetProperty("settings").GetProperty("engine").GetString());
        Assert.Equal(previous, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.DoesNotContain(previous, content, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    /// <summary>The database file is released before its prior inherited connection is restored.</summary>
    [Fact]
    public async Task SqliteFileDisposalRemovesOwnedDatabaseAndRestoresEnvironment()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        using var settings = new InheritedSettings();
        const string previous = "previous inherited connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previous);
        var options = new BenchmarkRunOptions
        {
            Engine = BenchmarkEngine.SqliteFile,
            ArtifactsPath = directory.Path,
        };

        options.ApplyEnvironment();
        var environment = await BenchmarkEnvironment.CreateAsync(options, CancellationToken.None);
        var databaseFile = new SqliteConnectionStringBuilder(BenchmarkEnvironment.ConnectionString).DataSource;
        var existed = File.Exists(databaseFile);

        // Act
        await environment.DisposeAsync();

        // Assert
        Assert.True(existed);
        Assert.False(File.Exists(databaseFile));
        Assert.False(File.Exists(databaseFile + "-wal"));
        Assert.False(File.Exists(databaseFile + "-shm"));
        Assert.Equal(previous, Environment.GetEnvironmentVariable(ConnectionVariable));
    }

    /// <summary>Pre-canceled startup never publishes a connection or acquires a database file.</summary>
    [Fact]
    public async Task CanceledEnvironmentStartupPreservesPreviousConnection()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        using var settings = new InheritedSettings();
        const string previous = "unmodified inherited connection";
        Environment.SetEnvironmentVariable(ConnectionVariable, previous);
        var options = new BenchmarkRunOptions
        {
            Engine = BenchmarkEngine.SqliteFile,
            ArtifactsPath = directory.Path,
        };

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => BenchmarkEnvironment.CreateAsync(options, cancellation.Token));

        // Assert
        var canceled = Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(previous, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    /// <summary>Unknown source directories fail explicitly rather than exporting an invented source identity.</summary>
    [Fact]
    public async Task ProvenanceRejectsDirectoryWithoutSourceRepository()
    {
        // Arrange
        using var directory = new TemporaryDirectory();

        // Act
        var error = await Record.ExceptionAsync(() => BenchmarkProvenance.ReadSourceIdentityAsync(
            directory.Path,
            CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Restores process-wide settings even when a provenance or resource assertion fails.</summary>
    private sealed class InheritedSettings : IDisposable
    {
        private readonly string? _options = Environment.GetEnvironmentVariable(OptionsVariable);
        private readonly string? _connection = Environment.GetEnvironmentVariable(ConnectionVariable);

        /// <inheritdoc />
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(OptionsVariable, _options);
            Environment.SetEnvironmentVariable(ConnectionVariable, _connection);
        }
    }

    /// <summary>Owns only the uniquely named temporary artifact directory created by one test.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = FilePath.Combine(
                FilePath.GetTempPath(),
                "nestedset-benchmark-tests-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        /// <inheritdoc />
        public void Dispose() => Directory.Delete(Path, true);
    }
}
