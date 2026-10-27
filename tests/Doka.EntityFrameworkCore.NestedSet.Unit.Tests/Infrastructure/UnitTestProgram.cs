using System.Text.Json;
using Xunit.Runner.InProc.SystemConsole;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Runs an isolated calibration before runner initialization, or the ordinary xUnit console runner.</summary>
internal static class UnitTestProgram
{
    /// <summary>Routes only the exact private calibration command outside the normal test runner.</summary>
    /// <param name="args">The calibration arguments or unchanged xUnit command-line arguments.</param>
    /// <returns>The calibration or xUnit process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (args is [ManagedHeapProbe.Command, var shape])
        {
            var measurement = ManagedHeapProbe.Measure(shape);
            var json = JsonSerializer.Serialize(measurement);
            await Console.Out.WriteLineAsync(json.AsMemory(), CancellationToken.None);

            return 0;
        }

        return await ConsoleRunner.Run(args);
    }
}
