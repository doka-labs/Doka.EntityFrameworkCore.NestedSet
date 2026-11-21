using System.ComponentModel;
using UserGroupsProgram = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups.Program;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

public sealed partial class UserSupervisorCommandTests
{
    /// <summary>
    /// The actual runner captures both streams and leaves its caller-owned process usable after reaping.
    /// </summary>
    /// <param name="exitCode">An ordinary success or an explicit child-command failure.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public async Task RunAsync_Runner_CapturesCompletedChildStreams(
        int exitCode
    )
    {
        // Arrange
        using var process = CreateBlockingProcess(exitCode);

        // Act
        var run = RunAsync(process, TestContext.Current.CancellationToken);
        await process.StandardInput.WriteLineAsync("release".AsMemory(), TestContext.Current.CancellationToken);
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
    public async Task RunAsync_Runner_ReapsChildBeforePropagatingCancellation()
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
        Assert.Equal(
            stop.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(error)
                .CancellationToken);

        Assert.True(process.HasExited);
    }

    /// <summary>A capture setup failure after launch still terminates and reaps the owned child.</summary>
    [Fact]
    public async Task RunAsync_Runner_ReapsChildBeforePropagatingCaptureFailure()
    {
        // Arrange
        using var process = CreateBlockingProcess(0);
        process.StartInfo.RedirectStandardError = false;

        // Act
        var error = await Record.ExceptionAsync(() => RunAsync(process, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("StandardError", error.Message, StringComparison.Ordinal);
        Assert.True(process.HasExited);
    }

    /// <summary>
    /// Cancellation before entry prevents process creation rather than launching work that already expired.
    /// </summary>
    [Fact]
    public async Task RunAsync_Runner_DoesNotStartChildForCanceledToken()
    {
        // Arrange
        using var process = CreateBlockingProcess(0);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => RunAsync(process, stop.Token));
        var associationError = Record.Exception(() => _ = process.Id);

        // Assert
        Assert.Equal(
            stop.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(error)
                .CancellationToken);

        Assert.IsType<InvalidOperationException>(associationError);
    }

    /// <summary>Failure to launch retains its native error without attempting to reap an unstarted process.</summary>
    [Fact]
    public async Task RunAsync_Runner_PreservesStartFailure()
    {
        // Arrange
        using var process = CreateBlockingProcess(0);
        process.StartInfo.FileName = Path.Combine(
            Path.GetTempPath(),
            Guid
                .NewGuid()
                .ToString("N"),
            "missing-child");

        // Act
        var error = await Record.ExceptionAsync(() => RunAsync(process, TestContext.Current.CancellationToken));
        var associationError = Record.Exception(() => _ = process.Id);

        // Assert
        Assert.IsType<Win32Exception>(error);
        Assert.IsType<InvalidOperationException>(associationError);
    }

    /// <summary>Runs ordinary sample behavior in process with invocation-owned streams and storage.</summary>
    /// <param name="directory">A test-owned directory for the fixed SQLite filename.</param>
    /// <param name="arguments">The exact console options for one invocation.</param>
    /// <param name="cancellationToken">The runner token passed through the complete invocation.</param>
    /// <returns>The exit code and separate output streams.</returns>
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string directory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await UserGroupsProgram.RunAsync(
            arguments.ToArray(),
            output,
            error,
            directory,
            cancellationToken);

        return (exitCode, output.ToString(), error.ToString());
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

        try
        {
            // WHY: Stream reads must drain after cancellation kills the child; the canceled forward token
            // must neither truncate its diagnostic output nor leave either read unobserved during cleanup.
            outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask.WaitAsync(cancellationToken);
            var error = await errorTask.WaitAsync(cancellationToken);

            return (process.ExitCode, output, error);
        }
        catch (Exception failure)
        {
            var cleanupFailures = new List<Exception>();

            // WHY: Child termination, reaping and stream observation are protected cleanup. The outer
            // runner owns hang protection; machine speed must not invalidate successful cleanup.
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
                await process.WaitForExitAsync(CancellationToken.None);
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

                throw new AggregateException(
                    "The sample command failed and its child cleanup also failed.",
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
        var startInfo = new ProcessStartInfo(
            OperatingSystem.IsWindows()
                ? "cmd.exe"
                : "/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(
            OperatingSystem.IsWindows()
                ? "/C"
                : "-c");

        startInfo.ArgumentList.Add(
            OperatingSystem.IsWindows()
                ? $"echo child-output& echo child-error 1>&2& set /p release=& exit /b {exitCode}"
                : $"printf 'child-output\\n'; printf 'child-error\\n' >&2; IFS= read -r release; exit {exitCode}");

        return new Process { StartInfo = startInfo };
    }

    /// <summary>Creates the prior sample database as test setup and stops on a failed seed command.</summary>
    /// <param name="directory">A test-owned directory for the sample's fixed SQLite filename.</param>
    /// <param name="arguments">The console options that produce the seed database.</param>
    /// <returns>A task that completes when a successful prior run is available for inspection.</returns>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task SeedAsync(
        string directory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAsync(directory, arguments, cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The UserGroups sample setup failed: " + result.Error);
        }
    }
}
