using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Calibrates the real collected-heap helper against independently counted, strongly held allocations.
/// </summary>
internal static class ManagedHeapProbe
{
    /// <summary>Identifies the exact private command handled before xUnit initializes.</summary>
    internal const string Command = "--nestedset-heap-calibration";

    /// <summary>Measures a warmed object shape without loading the test runner's transient state.</summary>
    /// <param name="shape">The independently counted small-object or small-array allocation shape.</param>
    /// <returns>The retained growth, allocation traffic and number of strongly held objects.</returns>
    internal static Measurement Measure(
        string shape
    )
    {
        var arrays = shape switch
        {
            "objects" => false,
            "arrays" => true,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown heap calibration shape."),
        };

        // WHY: Warm the same allocator/helper path before the baseline. Serialization and runner startup
        // happen afterward, so neither can add process-wide setup objects to the measured difference.
        _ = MeasureHeldObjects(arrays, 1_000);

        return MeasureHeldObjects(arrays, 100_000);
    }

    /// <summary>
    /// Executes this assembly's private probe without SDK builds, environment mutation or a new project.
    /// </summary>
    /// <param name="shape">The allocation shape measured in the independent process.</param>
    /// <param name="cancellationToken">The caller's token for forward waiting, never for child cleanup.</param>
    /// <returns>The child process's independent counters.</returns>
    internal static async Task<Measurement> RunIsolatedAsync(
        string shape,
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
        start.ArgumentList.Add(Command);
        start.ArgumentList.Add(shape);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The isolated heap calibration process did not start.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(1));
        using var cleanup = new CancellationTokenSource();

        // WHY: Cancellation must stop forward waiting without abandoning the streams before child termination.
        var output = process.StandardOutput.ReadToEndAsync(cleanup.Token);
        var errors = process.StandardError.ReadToEndAsync(cleanup.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var json = await output.WaitAsync(timeout.Token);
            var error = await errors.WaitAsync(timeout.Token);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Heap calibration exited with {process.ExitCode}: {error}");
            }

            return JsonSerializer.Deserialize<Measurement>(json);
        }
        catch (Exception failure)
        {
            // WHY: Recovery has its own deadline; the caller's expired token cannot prevent reaping or draining.
            cleanup.CancelAfter(TimeSpan.FromSeconds(10));
            var cleanupFailures = new List<Exception>();

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
                await process.WaitForExitAsync(cleanup.Token);
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures.Add(cleanupFailure);
            }

            foreach (var read in new[] { output, errors })
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
                ? new OperationCanceledException("The heap calibration was canceled.", failure, cancellationToken)
                : failure;

            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, original);

                throw new AggregateException("Heap calibration and child cleanup both failed.", cleanupFailures);
            }

            if (!ReferenceEquals(original, failure))
            {
                throw original;
            }

            throw;
        }
    }

    /// <summary>Measures only held allocations; the reference array is already included in the baseline.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Measurement MeasureHeldObjects(
        bool arrays,
        int count
    )
    {
        var held = new object[count];
        var baseline = ManagedHeapObservation.CollectedBytes();
        var before = GC.GetAllocatedBytesForCurrentThread();

        // WHY: Populate the already budgeted root array directly so only held objects enter the measured interval.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < held.Length; index++)
        {
            held[index] = arrays ? new byte[17] : new object();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var retained = ManagedHeapObservation.CollectedBytes() - baseline;
        var objects = held.Count(value => value is not null);
        GC.KeepAlive(held);

        return new Measurement(allocated, retained, objects);
    }

    /// <summary>Contains only scalar observations, never references to the retained object graph.</summary>
    /// <param name="Allocated">The independently counted allocation traffic of strongly held objects.</param>
    /// <param name="Retained">The actual helper's collected-heap difference.</param>
    /// <param name="Objects">The number of live references verified after the final collection.</param>
    internal readonly record struct Measurement(
        long Allocated,
        long Retained,
        int Objects
    );
}
