using System.ComponentModel;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Runs the actual UserGroups console entry point against isolated SQLite sample databases.</summary>
public sealed class UserSupervisorCommandTests
{
    /// <summary>The real supervisor command prints both group memberships and shadow positions after a move.</summary>
    [Fact]
    public async Task SupervisorScenarioShowsShadowCoordinatesAndRetainedGrantsAsync()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();

        // Act
        var result = await RunAsync(
            directory.DirectoryPath,
            ["--provider", "sqlite", "--scenario", "supervisors", "--details"]);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("After Taylor moves below the Finance director:", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Taylor - Software engineer (groups: API team, Finance)",
            result.Output,
            StringComparison.Ordinal);

        Assert.Contains("Morgan - Chief executive (groups: none)", result.Output, StringComparison.Ordinal);
        Assert.Contains("NestedSetPosition=0, Left=5, Right=6", result.Output, StringComparison.Ordinal);
        Assert.Contains("Filtered supervisor: Jordan (Director)", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Taylor keeps logs.read and invoices.read from two groups",
            result.Output,
            StringComparison.Ordinal);
    }

    /// <summary>Inspection displays the persisted supervisor tree without changing the SQLite file.</summary>
    [Fact]
    public async Task InspectionReadsSupervisorResultsWithoutMutationAsync()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();
        await SeedAsync(directory.DirectoryPath, ["--provider", "sqlite", "--scenario", "supervisors"]);
        var databasePath = Path.Combine(directory.DirectoryPath, "nestedset_sample_usergroups.sqlite");
        var before = await File.ReadAllBytesAsync(databasePath, CancellationToken.None);

        // Act
        var result = await RunAsync(directory.DirectoryPath, ["--provider", "sqlite", "--inspect", "--details"]);
        var after = await File.ReadAllBytesAsync(databasePath, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Stored supervisor tree: Morgan", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Taylor - Software engineer (groups: API team, Finance)",
            result.Output,
            StringComparison.Ordinal);

        Assert.Contains("NestedSetPosition=0, Left=5, Right=6", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    /// <summary>The default invocation keeps the four previous workflows beside the new fifth one.</summary>
    [Fact]
    public async Task AllScenariosIncludeSupervisorHierarchyAsync()
    {
        // Arrange
        await using var directory = new SampleLifecycleDirectory();

        // Act
        var result = await RunAsync(directory.DirectoryPath, ["--provider", "sqlite"]);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("INHERITANCE:", result.Output, StringComparison.Ordinal);
        Assert.Contains("COMMIT:", result.Output, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK:", result.Output, StringComparison.Ordinal);
        Assert.Contains("REJECTED:", result.Output, StringComparison.Ordinal);
        Assert.Contains("SUPERVISORS:", result.Output, StringComparison.Ordinal);
        Assert.Contains("Selected scenarios completed", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Design-time creation accepts every name the console advertises without connecting to a server.
    /// </summary>
    [Fact]
    public void DokaDesignTimeFactoryAcceptsSupervisorScenario()
    {
        // Arrange
        var factory = new UserGroupContextFactory();

        // Act
        using var context = factory.CreateDbContext(["--scenario", "supervisors"]);

        // Assert
        Assert.Equal("Doka.EntityFrameworkCore.MySql", context.Database.ProviderName);
    }

    /// <summary>
    /// The actual runner captures both streams and leaves its caller-owned process usable after reaping.
    /// </summary>
    /// <param name="exitCode">An ordinary success or an explicit child-command failure.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public async Task RunnerCapturesCompletedChildStreamsAsync(
        int exitCode
    )
    {
        // Arrange
        using var process = CreateBlockingProcess(exitCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var run = RunAsync(process, timeout.Token);
        await process.StandardInput.WriteLineAsync("release".AsMemory(), timeout.Token);
        var result = await run;

        // Assert
        Assert.True(process.HasExited);
        Assert.Equal(exitCode, process.ExitCode);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal("child-output", result.Output.Trim());
        Assert.Equal("child-error", result.Error.Trim());
    }

    /// <summary>
    /// Canceling a started stdin-blocked child terminates and reaps it before returning the original cancellation.
    /// </summary>
    [Fact]
    public async Task RunnerReapsChildBeforePropagatingCancellationAsync()
    {
        // Arrange
        using var process = CreateBlockingProcess(0);
        using var stop = new CancellationTokenSource();

        // Act
        // WHY: RunAsync starts synchronously before its first await; stdin remains open and unreleased.
        var run = RunAsync(process, stop.Token);
        await stop.CancelAsync();
        var error = await Record.ExceptionAsync(async () => await run);

        // Assert
        Assert.Equal(stop.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.True(process.HasExited);
    }

    /// <summary>A capture setup failure after launch still terminates and reaps the owned child.</summary>
    [Fact]
    public async Task RunnerReapsChildBeforePropagatingCaptureFailureAsync()
    {
        // Arrange
        using var process = CreateBlockingProcess(0);
        process.StartInfo.RedirectStandardError = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var error = await Record.ExceptionAsync(() => RunAsync(process, timeout.Token));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("StandardError", error.Message, StringComparison.Ordinal);
        Assert.True(process.HasExited);
    }

    /// <summary>
    /// Cancellation before entry prevents process creation rather than launching work that already expired.
    /// </summary>
    [Fact]
    public async Task RunnerDoesNotStartChildForCanceledTokenAsync()
    {
        // Arrange
        using var process = CreateBlockingProcess(0);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => RunAsync(process, stop.Token));
        var associationError = Record.Exception(() => _ = process.Id);

        // Assert
        Assert.Equal(stop.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.IsType<InvalidOperationException>(associationError);
    }

    /// <summary>Failure to launch retains its native error without attempting to reap an unstarted process.</summary>
    [Fact]
    public async Task RunnerPreservesStartFailureAsync()
    {
        // Arrange
        using var process = CreateBlockingProcess(0);
        process.StartInfo.FileName = Path.Combine(Path.GetTempPath(),
            Guid
                .NewGuid()
                .ToString("N"),
            "missing-child");

        // Act
        var error = await Record.ExceptionAsync(() => RunAsync(process, CancellationToken.None));
        var associationError = Record.Exception(() => _ = process.Id);

        // Assert
        Assert.IsType<Win32Exception>(error);
        Assert.IsType<InvalidOperationException>(associationError);
    }

    /// <summary>Starts only the already-built sample assembly and returns its captured process result.</summary>
    /// <param name="directory">A test-owned directory for the sample's fixed SQLite filename.</param>
    /// <param name="arguments">The exact console options for one invocation.</param>
    /// <returns>The exit code and separate standard streams.</returns>
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string directory,
        IReadOnlyList<string> arguments
    )
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(typeof(User).Assembly.Location);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["NESTEDSET_SAMPLE_SQLITE_DIRECTORY"] = directory;

        using var process = new Process();
        process.StartInfo = startInfo;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        return await RunAsync(process, timeout.Token);
    }

    /// <summary>
    /// Owns a launched child's lifetime and stream reads; the caller retains ownership of its process wrapper.
    /// </summary>
    /// <param name="process">
    /// The configured, unstarted process; its caller must dispose it after this method completes.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels forward waiting without abandoning termination, reaping or stream observation.
    /// </param>
    /// <returns>The exit code and separate standard streams after the child has exited.</returns>
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        Process process,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: Process.Start launches synchronously; asynchronous waiting and stream reads avoid blocking the test.
        if (!process.Start())
        {
            throw new InvalidOperationException("The UserGroups sample process did not start.");
        }

        Task<string>? outputTask = null;
        Task<string>? errorTask = null;
        using var cleanup = new CancellationTokenSource();

        try
        {
            // WHY: Stream reads must drain after cancellation kills the child; the canceled forward token
            // must neither truncate its diagnostic output nor leave either read unobserved during cleanup.
            // The independent cleanup token is bounded only if termination or capture fails.
            outputTask = process.StandardOutput.ReadToEndAsync(cleanup.Token);
            errorTask = process.StandardError.ReadToEndAsync(cleanup.Token);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask.WaitAsync(cancellationToken);
            var error = await errorTask.WaitAsync(cancellationToken);

            return (process.ExitCode, output, error);
        }
        catch (Exception failure)
        {
            var cleanupFailures = new List<Exception>();

            // WHY: A failed kill or inherited open pipe must not leave the test waiting forever. This
            // deadline belongs to recovery and remains independent of the expired forward operation.
            cleanup.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // WHY: The child may exit between HasExited and Kill; it still must be reaped below.
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures.Add(cleanupFailure);
            }

            try
            {
                await process.WaitForExitAsync(cleanup.Token);
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures.Add(cleanupFailure);
            }

            foreach (var read in new[] { outputTask, errorTask })
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

                throw new AggregateException("The sample command failed and its child cleanup also failed.",
                    cleanupFailures);
            }

            throw;
        }
    }

    /// <summary>Creates a child that emits separate streams and blocks on stdin until explicitly released.</summary>
    /// <param name="exitCode">The exit code returned after the caller writes a release line.</param>
    /// <returns>An unstarted process wrapper owned by the calling test.</returns>
    private static Process CreateBlockingProcess(
        int exitCode
    )
    {
        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/C" : "-c");
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows()
            ? $"echo child-output& echo child-error 1>&2& set /p release=& exit /b {exitCode}"
            : $"printf 'child-output\\n'; printf 'child-error\\n' >&2; IFS= read -r release; exit {exitCode}");

        return new Process { StartInfo = startInfo };
    }

    /// <summary>Creates the prior sample database as test setup and stops on a failed seed command.</summary>
    /// <param name="directory">A test-owned directory for the sample's fixed SQLite filename.</param>
    /// <param name="arguments">The console options that produce the seed database.</param>
    /// <returns>A task that completes when a successful prior run is available for inspection.</returns>
    private static async Task SeedAsync(
        string directory,
        IReadOnlyList<string> arguments
    )
    {
        var result = await RunAsync(directory, arguments);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The UserGroups sample setup failed: " + result.Error);
        }
    }
}
