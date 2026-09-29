using System.Reflection;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Runs the same operations once for command diagnostics, without a timer, sampler or acceptance budgets.
/// </summary>
internal static class BenchmarkDiagnostics
{
    /// <summary>Executes selected feature methods and writes their operation-only command observations.</summary>
    /// <param name="options">The provider, input parameters and output directory.</param>
    /// <param name="selection">The actual native cases already validated by resource preflight.</param>
    /// <param name="cancellationToken">The token used for reporting and diagnostic dispatch.</param>
    internal static async Task RunAsync(
        BenchmarkRunOptions options,
        BenchmarkSelection selection,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selection);

        var results = new List<DiagnosticResult>();

        // WHY: Multiple native jobs repeat one operation/input case, so untimed diagnostics execute it once.
        var cases = selection.DatabaseCases.DistinctBy(benchmarkCase =>
            (benchmarkCase.Descriptor.WorkloadMethod, benchmarkCase.Parameters.PrintInfo));

        foreach (var benchmarkCase in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = benchmarkCase.Descriptor.Type;
            var method = benchmarkCase.Descriptor.WorkloadMethod;
            var name = type.FullName + "." + method.Name;
            var benchmark = (DatabaseBenchmark)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("The diagnostic benchmark must have a public constructor."));

            foreach (var parameter in benchmarkCase.Parameters.Items)
            {
                var property = type.GetProperty(parameter.Name)
                    ?? throw new InvalidOperationException(
                        "The diagnostic parameter must map to a public benchmark property.");

                property.SetValue(benchmark, parameter.Value);
            }

            var scenario = BenchmarkSelection.CreateScenario(benchmarkCase);

            try
            {
                benchmark.Initialize();
                await benchmark.PrepareAsync();
                benchmark.Observation.Reset();

                // WHY: Reflection belongs to untimed discovery only; retain the original operation exception.
                var task = method.Invoke(
                        benchmark,
                        BindingFlags.DoNotWrapExceptions,
                        null,
                        null,
                        null) as Task
                    ?? throw new InvalidOperationException("Database benchmark methods must return an awaited Task.");

                await task;
                var observation = benchmark.Observation;
                var result = new DiagnosticResult(
                    name,
                    options.Engine,
                    scenario.Nodes,
                    scenario.Shape,
                    scenario.Trees,
                    scenario.Tracked,
                    observation.Commands,
                    observation.Updates,
                    observation.UpdatedRows);

                await benchmark.VerifyAsync();
                results.Add(result);
            }
            catch
            {
                var message = $"Diagnostic failed: {name}; engine={options.Engine}; "
                    + $"nodes={scenario.Nodes}; shape={scenario.Shape}; "
                    + $"trees={scenario.Trees}; tracked={scenario.Tracked}.";

                await Console.Error.WriteLineAsync(message.AsMemory(), cancellationToken);

                throw;
            }
            finally
            {
                await benchmark.CleanupAsync();
            }
        }

        if (results.Count == 0)
        {
            throw new ArgumentException("No database benchmark matched the diagnostic selection.");
        }

        Directory.CreateDirectory(options.ArtifactsPath);
        await using var stream = new FileStream(
            Path.Combine(options.ArtifactsPath, "command-diagnostics.json"),
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Options = FileOptions.Asynchronous,
            });

        await JsonSerializer.SerializeAsync(
            stream,
            new
            {
                Kind = "UntimedCommandDiagnostics",
                Description = "Completed commands exclude transaction APIs; hierarchy update commands "
                    + "and affected rows include only completed non-query UPDATE observations, not disk writes.",
                Results = results,
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>Contains structural observations without any latency, allocation or budget verdict.</summary>
    /// <param name="Benchmark">The fully qualified operation name.</param>
    /// <param name="Engine">The provider selected for this diagnostic pass.</param>
    /// <param name="Nodes">The prepared forest size.</param>
    /// <param name="Shape">The prepared adjacency distribution.</param>
    /// <param name="Trees">The number of independent prepared trees.</param>
    /// <param name="Tracked">The unrelated prepared tracker population.</param>
    /// <param name="Commands">The completed command count, excluding transaction API calls.</param>
    /// <param name="HierarchyUpdates">The completed non-query hierarchy UPDATE command count.</param>
    /// <param name="AffectedRows">The provider-reported rows from those non-query UPDATE commands.</param>
    private sealed record DiagnosticResult(
        string Benchmark,
        BenchmarkEngine Engine,
        int Nodes,
        BenchmarkShape Shape,
        int Trees,
        int Tracked,
        int Commands,
        int HierarchyUpdates,
        long AffectedRows
    );
}
