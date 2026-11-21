using System.Globalization;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.Results;
using FilePath = System.IO.Path;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class BenchmarkProvenanceTests
{
    /// <summary>A core observation records its run identity without claiming database acquisition.</summary>
    [Fact]
    public async Task WriteAsync_CoreProvenance_HasNoDatabaseMetadata()
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

        var started = new DateTimeOffset(
            2026,
            9,
            29,
            1,
            2,
            3,
            TimeSpan.Zero);

        // Act
        await BenchmarkProvenance.WriteAsync(options, null, started, [], "completed", CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(
            Path.Combine(directory.Path, "provenance.json"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(content);
        var metadata = document.RootElement;

        Assert.Equal(
            JsonValueKind.Null,
            metadata.GetProperty("database")
                .ValueKind);

        Assert.Equal(
            started,
            metadata
                .GetProperty("createdAtUtc")
                .GetDateTimeOffset());

        Assert.Equal(
            "completed",
            metadata
                .GetProperty("outcome")
                .GetString());

        Assert.Equal(
            "MySql",
            metadata
                .GetProperty("settings")
                .GetProperty("engine")
                .GetString());

        Assert.Equal(previous, Environment.GetEnvironmentVariable(ConnectionVariable));
        Assert.DoesNotContain(previous, content, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    /// <summary>Native report metadata retains explicit toolchains without requiring optional job settings.</summary>
    /// <param name="explicitToolchain">Whether the job selects its toolchain instead of delegating to BDN.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAsync_NativeJobProvenance_PreservesOptionalToolchain(
        bool explicitToolchain
    )
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var options = new BenchmarkRunOptions
        {
            ArtifactsPath = directory.Path,
        };

        var job = Job.ShortRun;

        if (explicitToolchain)
        {
            job = job.WithToolchain(CsProjCoreToolchain.NetCoreApp10_0);
        }

        var configuration = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(job);

        using var native = BenchmarkConverter.TypeToBenchmarks(
            typeof(CompositionConstructionBenchmarks),
            configuration);

        var benchmark = native.BenchmarksCases[0];
        var generated = GenerateResult.Success(ArtifactsPaths.Empty, []);

        // WHY: A native failed report exercises metadata without inventing workload measurements or spawning BDN.
        var report = new BenchmarkReport(false, benchmark, generated, BuildResult.Success(generated), [], []);

        var summary = new Summary(
            "optional-toolchain",
            [report],
            HostEnvironmentInfo.GetCurrent(),
            directory.Path,
            Path.Combine(directory.Path, "native.log"),
            TimeSpan.Zero,
            CultureInfo.InvariantCulture,
            [],
            []);

        // Act
        await BenchmarkProvenance.WriteAsync(
            options,
            null,
            DateTimeOffset.UtcNow,
            [summary],
            "invalid",
            cancellationToken: CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(
            FilePath.Combine(directory.Path, "provenance.json"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(content);
        var metadata = document.RootElement;
        var nativeJob = Assert.Single(
            metadata
                .GetProperty("jobs")
                .EnumerateArray());

        var toolchain = nativeJob
            .GetProperty("job")
            .GetProperty("toolchain");

        Assert.Equal(
            "invalid",
            metadata
                .GetProperty("outcome")
                .GetString());

        Assert.False(
            metadata
                .GetProperty("sourcesChangedDuringRun")
                .GetBoolean());

        Assert.Equal(
            typeof(CompositionConstructionBenchmarks).FullName,
            nativeJob
                .GetProperty("benchmark")
                .GetString());

        Assert.Equal(
            benchmark.Descriptor.WorkloadMethod.Name,
            nativeJob
                .GetProperty("method")
                .GetString());

        Assert.Equal(
            benchmark.Parameters.PrintInfo,
            nativeJob
                .GetProperty("parameters")
                .GetString());

        Assert.False(
            nativeJob
                .GetProperty("success")
                .GetBoolean());

        if (explicitToolchain)
        {
            Assert.Equal(CsProjCoreToolchain.NetCoreApp10_0.Name, toolchain.GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, toolchain.ValueKind);
        }
    }
}
