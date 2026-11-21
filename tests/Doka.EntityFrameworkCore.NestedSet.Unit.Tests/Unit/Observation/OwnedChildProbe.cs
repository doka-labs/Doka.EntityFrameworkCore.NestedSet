namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns independent child processes used only for cancellation and failure-cleanup controls.</summary>
internal static class OwnedChildProbe
{
    /// <summary>Identifies the child that stays live until its owner cancels and reaps it.</summary>
    internal const string CancellationCommand = "--nestedset-owned-child-wait";

    /// <summary>Identifies the child that returns a deliberate failure before runner initialization.</summary>
    internal const string FailureCommand = "--nestedset-owned-child-failure";

    /// <summary>Starts a live child so cleanup can be tested without a machine-speed deadline.</summary>
    /// <param name="started">Receives the owned process after creation and before cancellable waiting.</param>
    /// <param name="cancellationToken">Cancels forward waiting, never cleanup.</param>
    /// <returns>The interrupted task, completed only after owned process and stream cleanup.</returns>
    internal static Task RunCancellationControlAsync(
        Action<Process> started,
        CancellationToken cancellationToken
    ) => RunChildAsync(CancellationCommand, "wait", started, cancellationToken);

    /// <summary>Starts a deliberately failing child and preserves its output after complete resource cleanup.</summary>
    /// <param name="marker">The synthetic child diagnostic that must survive failure handling.</param>
    /// <param name="cancellationToken">Cancels forward waiting, never cleanup.</param>
    /// <returns>The failed task after process and stream ownership have ended.</returns>
    internal static Task RunFailureControlAsync(
        string marker,
        CancellationToken cancellationToken
    ) => RunChildAsync(FailureCommand, marker, null, cancellationToken);

    /// <summary>Owns the child and drains both streams before releasing its process resources on every exit.</summary>
    private static async Task RunChildAsync(
        string command,
        string shape,
        Action<Process>? started,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        start.ArgumentList.Add(typeof(UnitTestProgram).Assembly.Location);
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(shape);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The isolated owned child process did not start.");

        // WHY: Readers belong to the child, not the cancellable wait. Termination and draining must finish
        // before their process is disposed; the outer runner alone owns hang protection.
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            started?.Invoke(process);
            await process.WaitForExitAsync(cancellationToken);
            _ = await output.WaitAsync(cancellationToken);
            var error = await errors.WaitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Owned child exited with {process.ExitCode}: {error}");
            }

            return;
        }
        catch (Exception failure)
        {
            // WHY: An expired request cannot interrupt reaping or stream draining after the owned child is killed.
            List<Exception> cleanupFailures = [];

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // WHY: Exit can race the kill; the child still needs the wait below.
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

            Task<string>[] reads = [output, errors];

            foreach (var read in reads)
            {
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

            var original = failure is OperationCanceledException && cancellationToken.IsCancellationRequested
                ? new OperationCanceledException("The owned child was canceled.", failure, cancellationToken)
                : failure;

            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, original);

                throw new AggregateException("Owned child and child cleanup both failed.", cleanupFailures);
            }

            if (!ReferenceEquals(original, failure))
            {
                throw original;
            }

            throw;
        }
    }

}
