namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns native case discovery shared by resource preflight and untimed diagnostics.</summary>
internal sealed class BenchmarkSelection : IDisposable
{
    private readonly BenchmarkRunInfo[] _runInfos;

    /// <summary>Retains native parameter ownership until preflight, diagnostics or measurement has completed.</summary>
    /// <param name="runInfos">The selected native types, cases and effective configurations.</param>
    private BenchmarkSelection(
        BenchmarkRunInfo[] runInfos
    )
    {
        _runInfos = runInfos;
        DatabaseCases = runInfos
            .SelectMany(runInfo => runInfo.BenchmarksCases)
            .Where(benchmarkCase => typeof(DatabaseBenchmark).IsAssignableFrom(benchmarkCase.Descriptor.Type))
            .ToArray();
    }

    /// <summary>Gets the selected native database cases, including their actual parameters and jobs.</summary>
    internal IReadOnlyList<BenchmarkCase> DatabaseCases { get; }

    /// <summary>Gets whether any selected case requires a launcher-owned database.</summary>
    internal bool RequiresDatabase => DatabaseCases.Count > 0;

    /// <summary>Checks native parsed selectors before the framework's inaccessible interactive prompt.</summary>
    /// <param name="options">The native options after aliases and response files have been parsed.</param>
    /// <returns>Whether a native filter, category or attribute explicitly selects execution cases.</returns>
    internal static bool HasExplicitSelection(
        CommandLineOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        // WHY: BDN 0.15.8 exposes selector collections, but its UserProvidedFilters and prompt are internal.
        return options.Filters.Any()
            || options.AttributeNames.Any()
            || options.AllCategories.Any()
            || options.AnyCategories.Any();
    }

    /// <summary>Discovers and validates only the native cases selected for this invocation.</summary>
    /// <param name="configuration">The union of the launcher and parsed native configurations.</param>
    /// <param name="diagnostics">Whether execution requires at least one selected database operation.</param>
    /// <returns>The native selection owner, released after all selected execution has completed.</returns>
    /// <exception cref="ArgumentException">Selection is empty or a selected database scenario is invalid.</exception>
    internal static BenchmarkSelection Discover(
        IConfig configuration,
        bool diagnostics
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var (allTypesValid, runnable) = TypeFilter.GetTypesWithRunnableBenchmarks(
            [],
            [typeof(Program).Assembly],
            ConsoleLogger.Default);

        if (!allTypesValid)
        {
            throw new ArgumentException("The benchmark assembly contains an invalid runnable type.");
        }

        var selection = new BenchmarkSelection(TypeFilter.Filter(configuration, runnable));

        try
        {
            if (!selection._runInfos.Any(runInfo => runInfo.BenchmarksCases.Length > 0))
            {
                throw new ArgumentException("No benchmark matched the native selection.");
            }

            if (diagnostics && !selection.RequiresDatabase)
            {
                throw new ArgumentException("No database benchmark matched the diagnostic selection.");
            }

            foreach (var benchmarkCase in selection.DatabaseCases)
            {
                var scenario = CreateScenario(benchmarkCase);

                try
                {
                    scenario.Validate();
                }
                catch (ArgumentException exception)
                {
                    var name = benchmarkCase.Descriptor.Type.Name + "." + benchmarkCase.Descriptor.WorkloadMethod.Name;

                    throw new ArgumentException(
                        $"Selected database scenario {name} requires at least 10 nodes per tree; "
                        + $"nodes={scenario.Nodes}; shape={scenario.Shape}; "
                        + $"trees={scenario.Trees}; tracked={scenario.Tracked}.",
                        exception);
                }
            }

            return selection;
        }
        catch
        {
            selection.Dispose();

            throw;
        }
    }

    /// <summary>Reads expanded native parameters, including a cross-tree family's effective tree count.</summary>
    /// <param name="benchmarkCase">The selected database operation and parameter combination.</param>
    /// <returns>The exact scenario that the database fixture will prepare.</returns>
    internal static BenchmarkScenario CreateScenario(
        BenchmarkCase benchmarkCase
    ) => new(
        (int)benchmarkCase.Parameters[nameof(DatabaseBenchmark.Nodes)],
        (BenchmarkShape)benchmarkCase.Parameters[nameof(DatabaseBenchmark.Shape)],
        (int)benchmarkCase.Parameters[nameof(DatabaseBenchmark.Trees)],
        (int)benchmarkCase.Parameters[nameof(DatabaseBenchmark.Tracked)]);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var runInfo in _runInfos)
        {
            runInfo.Dispose();
        }
    }
}
