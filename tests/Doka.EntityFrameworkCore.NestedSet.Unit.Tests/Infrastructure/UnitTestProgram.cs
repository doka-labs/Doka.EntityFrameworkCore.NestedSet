using Xunit.Runner.InProc.SystemConsole;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Runs exact private lifecycle controls before runner initialization, or the ordinary xUnit runner.</summary>
internal static class UnitTestProgram
{
    /// <summary>Routes only the two owned-process controls outside the normal test runner.</summary>
    /// <param name="args">The lifecycle arguments or unchanged xUnit command-line arguments.</param>
    /// <returns>The deliberate control status or ordinary xUnit process exit code.</returns>
    public static async Task<int> Main(
        string[] args
    )
    {
        if (args is [OwnedChildProbe.CancellationCommand, "wait"])
        {
            // WHY: This control cannot finish before cancellation on a faster machine. Its parent owns
            // termination, exit observation and stream draining before releasing the native process handle.
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);

            return 0;
        }

        if (args is [OwnedChildProbe.FailureCommand, var marker])
        {
            await Console.Error.WriteLineAsync(marker.AsMemory(), CancellationToken.None);

            return 7;
        }

        return await ConsoleRunner.Run(args);
    }
}
