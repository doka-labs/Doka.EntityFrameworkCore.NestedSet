using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Evaluates the real repository configuration in an exclusively owned, package-free project.</summary>
internal sealed class BuildConfigurationProbe : IDisposable
{
    private readonly string _directory;
    private bool _disposed;

    /// <summary>Keeps probe files separate from the product's build and restore outputs.</summary>
    private BuildConfigurationProbe(
        string directory
    )
    {
        _directory = directory;
        ProjectDirectory = Path.Combine(directory, "project");
    }

    /// <summary>Gets the case-owned project directory used for actual SDK evaluation.</summary>
    internal string ProjectDirectory { get; }

    /// <summary>Copies the actual pinned toolchain and configuration without restoring or compiling.</summary>
    internal static async Task<BuildConfigurationProbe> CreateAsync(
        CancellationToken cancellationToken
    )
    {
        var probe = new BuildConfigurationProbe(Path.Combine(Path.GetTempPath(), "nestedset-build-" + Guid.NewGuid()));
        Directory.CreateDirectory(probe.ProjectDirectory);

        try
        {
            var repository = FindRepository();
            string[] inputs = ["Directory.Build.props", "NuGet.config", "global.json"];

            foreach (var input in inputs)
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(repository, input), cancellationToken);
                await File.WriteAllBytesAsync(Path.Combine(probe.ProjectDirectory, input), bytes, cancellationToken);
            }

            await probe.WriteProjectFileAsync(
                "Boundary.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\" />",
                cancellationToken);

            return probe;
        }
        catch (Exception failure)
        {
            try
            {
                probe.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Build probe initialization and cleanup both failed.",
                    failure,
                    cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>Writes a trusted case input beneath the owned project root.</summary>
    internal async Task WriteProjectFileAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken
    )
    {
        var path = Path.Combine(ProjectDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    /// <summary>Adds a conflicting inherited source without changing the developer's NuGet settings.</summary>
    internal Task WriteParentSourceAsync(
        CancellationToken cancellationToken
    ) => File.WriteAllTextAsync(
        Path.Combine(_directory, "NuGet.config"),
        "<configuration><packageSources><add key=\"unrelated-parent\" "
        + "value=\"https://unrelated.invalid/v3/index.json\" /></packageSources>"
        + "<disabledPackageSources><add key=\"nuget.org\" value=\"true\" /></disabledPackageSources></configuration>",
        cancellationToken);

    /// <summary>Uses MSBuild's own evaluated-property/item JSON rather than parsing build source text.</summary>
    internal async Task<JsonDocument> EvaluateAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var output = await RunAsync(["msbuild", "Boundary.csproj", "-nologo", .. arguments], null, cancellationToken);

        return JsonDocument.Parse(output);
    }

    /// <summary>Evaluates explicit output ownership against a conflicting inherited environment value.</summary>
    internal async Task<JsonDocument> EvaluateArtifactsAsync(
        string? environmentPath,
        string? globalPath,
        CancellationToken cancellationToken
    )
    {
        string[] arguments =
        [
            "msbuild", "Boundary.csproj", "-nologo", "-getProperty:ArtifactsPath,MSBuildProjectDirectory",
            .. globalPath is null ? Array.Empty<string>() : ["-p:ArtifactsPath=" + globalPath],
        ];

        var output = await RunAsync(arguments, environmentPath, cancellationToken);

        return JsonDocument.Parse(output);
    }

    /// <summary>Lists the effective source hierarchy without contacting any package server.</summary>
    internal Task<string> ListSourcesAsync(
        CancellationToken cancellationToken
    ) => RunAsync(["nuget", "list", "source", "--format", "short"], null, cancellationToken);

    /// <summary>Removes only the exclusively owned probe tree after its child has fully completed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Directory.Delete(_directory, recursive: true);
        _disposed = true;
    }

    /// <summary>Runs only the requested diagnostic SDK command and observes all owned process work.</summary>
    private async Task<string> RunAsync(
        string[] arguments,
        string? artifactsEnvironment,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (artifactsEnvironment is not null)
        {
            start.Environment["ArtifactsPath"] = artifactsEnvironment;
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The SDK probe did not start.");

        // WHY: These drains outlive a canceled wait so the owned process can be reaped without abandoned IO.
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            try
            {
                await CompleteProcessAsync(process, output, error);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Build probe execution and cleanup both failed.", failure, cleanupFailure);
            }

            throw;
        }

        var result = await output;
        var diagnostic = await error;
        Assert.True(process.ExitCode == 0, $"SDK probe exited {process.ExitCode}: {diagnostic}\n{result}");

        return result;
    }

    /// <summary>Reaps an owned child and drains its streams even when the requesting test is canceled.</summary>
    private static async Task CompleteProcessAsync(
        Process process,
        Task<string> output,
        Task<string> error
    )
    {
        List<Exception>? failures = null;

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
                    // WHY: The owned child can finish between the observation and its termination request.
                }
            }
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        // WHY: Termination and wait failures must not abandon either redirected stream or hide its failure.
        var drains = Task.WhenAll(output, error);

        try
        {
            await drains;
        }
        catch (Exception failure)
        {
            if (drains.Exception is { } aggregate)
            {
                (failures ??= []).AddRange(aggregate.InnerExceptions);
            }
            else
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Build probe cleanup failed.", failures);
        }
    }

    /// <summary>Finds repository inputs without binding the test to a developer's checkout path.</summary>
    private static string FindRepository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.NestedSet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The repository configuration inputs could not be found.");
    }
}
