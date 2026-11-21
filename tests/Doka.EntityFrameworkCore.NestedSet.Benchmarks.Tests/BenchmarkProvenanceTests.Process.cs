using System.ComponentModel;
using System.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class BenchmarkProvenanceTests
{
    /// <summary>Ordinary metadata completion observes both streams before returning its actual exit status.</summary>
    /// <param name="exitCode">An ordinary success or an explicit unsuccessful metadata command.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public async Task RunProcessAsync_CompletedChild_ObservesStreamsAndExit(
        int exitCode
    )
    {
        // Arrange
        using var process = CreateMetadataProcess(exitCode);

        // Act
        var run = BenchmarkProvenance.RunProcessAsync(process, TestContext.Current.CancellationToken);
        await process.StandardInput.WriteLineAsync("release".AsMemory(), TestContext.Current.CancellationToken);
        var result = await run;

        // Assert
        Assert.True(process.HasExited);
        Assert.Equal(exitCode, process.ExitCode);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal("metadata-output", result.Output.Trim());
        Assert.Empty(await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Caller cancellation terminates and reaps a real stdin-blocked child before it propagates.</summary>
    [Fact]
    public async Task RunProcessAsync_CanceledChild_ReapsBeforePropagatingOriginalToken()
    {
        // Arrange
        using var process = CreateMetadataProcess(0);
        using var cancellation = new CancellationTokenSource();

        // Act
        // WHY: The helper launches synchronously before waiting; its child remains blocked on unreleased stdin.
        var run = BenchmarkProvenance.RunProcessAsync(process, cancellation.Token);
        await cancellation.CancelAsync();
        var failure = await Record.ExceptionAsync(async () => await run);

        // Assert
        Assert.Equal(cancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(failure).CancellationToken);
        Assert.True(process.HasExited);
        Assert.Empty(await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A failure between stream acquisitions still reaps the launched child and observes its first read.
    /// </summary>
    [Fact]
    public async Task RunProcessAsync_CaptureFailure_ReapsBeforePropagatingOriginalFailure()
    {
        // Arrange
        using var process = CreateMetadataProcess(0);
        process.StartInfo.RedirectStandardError = false;

        // Act
        var failure = await Record.ExceptionAsync(() =>
            BenchmarkProvenance.RunProcessAsync(process, TestContext.Current.CancellationToken));

        // Assert
        var captureFailure = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("StandardError", captureFailure.Message, StringComparison.Ordinal);
        Assert.True(process.HasExited);
        Assert.Empty(await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Already canceled forward work never acquires a child process.</summary>
    [Fact]
    public async Task RunProcessAsync_PreCanceledToken_DoesNotStartChild()
    {
        // Arrange
        using var process = CreateMetadataProcess(0);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var failure = await Record.ExceptionAsync(() =>
            BenchmarkProvenance.RunProcessAsync(process, cancellation.Token));
        var associationFailure = Record.Exception(() => _ = process.Id);

        // Assert
        Assert.Equal(cancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(failure).CancellationToken);
        Assert.IsType<InvalidOperationException>(associationFailure);
    }

    /// <summary>A native launch failure is preserved without cleanup of an unstarted child.</summary>
    [Fact]
    public async Task RunProcessAsync_StartFailure_PreservesNativeFailure()
    {
        // Arrange
        using var process = CreateMetadataProcess(0);
        process.StartInfo.FileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-child");

        // Act
        var failure = await Record.ExceptionAsync(() =>
            BenchmarkProvenance.RunProcessAsync(process, TestContext.Current.CancellationToken));

        var associationFailure = Record.Exception(() => _ = process.Id);

        // Assert
        Assert.IsType<Win32Exception>(failure);
        Assert.IsType<InvalidOperationException>(associationFailure);
    }

    /// <summary>Creates a real child that supplies both streams and waits for an explicit stdin release.</summary>
    private static Process CreateMetadataProcess(
        int exitCode
    )
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(OperatingSystem.IsWindows() ? "/C" : "-c");
        start.ArgumentList.Add(OperatingSystem.IsWindows()
            ? $"echo metadata-output& echo metadata-error 1>&2& set /p release=& exit /b {exitCode}"
            : $"printf 'metadata-output\\n'; printf 'metadata-error\\n' >&2; IFS= read -r release; exit {exitCode}");

        return new Process { StartInfo = start };
    }
}
