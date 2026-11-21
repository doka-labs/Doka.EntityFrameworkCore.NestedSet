using NativeTypeFilter = BenchmarkDotNet.Running.TypeFilter;

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
            .Where(benchmarkCase => typeof(IDatabaseBenchmark).IsAssignableFrom(benchmarkCase.Descriptor.Type))
            .ToArray();
    }

    /// <summary>Gets the selected native database cases, including their actual parameters and jobs.</summary>
    internal IReadOnlyList<BenchmarkCase> DatabaseCases { get; }

    /// <summary>Gets whether any selected case requires a launcher-owned database.</summary>
    internal bool RequiresDatabase => DatabaseCases.Count > 0;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var runInfo in _runInfos)
        {
            runInfo.Dispose();
        }
    }

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

        var (allTypesValid, runnable) = NativeTypeFilter.GetTypesWithRunnableBenchmarks(
            [],
            [typeof(Program).Assembly],
            ConsoleLogger.Default);

        if (!allTypesValid)
        {
            throw new ArgumentException("The benchmark assembly contains an invalid runnable type.");
        }

        var selection = new BenchmarkSelection(NativeTypeFilter.Filter(configuration, runnable));

        try
        {
            if (selection._runInfos.All(runInfo => runInfo.BenchmarksCases.Length == 0))
            {
                throw new ArgumentException("No benchmark matched the native selection.");
            }

            if (diagnostics && !selection.RequiresDatabase)
            {
                throw new ArgumentException("No database benchmark matched the diagnostic selection.");
            }

            // WHY: The owned discovery snapshot is indexable and does not change during validation.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var caseIndex = 0; caseIndex < selection.DatabaseCases.Count; caseIndex++)
            {
                var benchmarkCase = selection.DatabaseCases[caseIndex];

                var scenario = CreateScenario(benchmarkCase);

                try
                {
                    if (benchmarkCase.Descriptor.Type == typeof(NativeGuardBenchmarks))
                    {
                        // WHY: Guard baselines contain fewer than ten nodes and protect independent root trees.
                        NativeGuardBenchmarks.ValidateCase((NativeGuardCase)benchmarkCase.Parameters[nameof(NativeGuardBenchmarks.Case)]);
                    }
                    else if (benchmarkCase.Descriptor.Type == typeof(CleanMutationBoundaryBenchmarks))
                    {
                        // WHY: This workload reserves an empty tree and contains only unrelated payload entries.
                        ArgumentOutOfRangeException.ThrowIfNegative(scenario.Tracked);
                    }
                    else if (benchmarkCase.Descriptor.Type == typeof(NativeTrackerWriteBenchmarks))
                    {
                        // WHY: These original workloads contain one existing root, not a ten-node forest.
                        ArgumentOutOfRangeException.ThrowIfNegative(scenario.Tracked);
                    }
                    else
                    {
                        scenario.Validate();
                    }
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

                if (benchmarkCase.Descriptor.Type == typeof(UserGroupSelectionBenchmarks)
                    && (int)benchmarkCase.Parameters[nameof(UserGroupSelectionBenchmarks.Origins)] > scenario.Nodes)
                {
                    // WHY: Every visible group, including a root with a NotFound supervisor outcome, is a valid
                    // membership origin. More origins than persisted groups must fail before Docker is acquired.
                    throw new ArgumentException("UserGroups selection requires Origins <= Nodes.");
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
    )
    {
        if (benchmarkCase.Descriptor.Type == typeof(NativeGuardBenchmarks))
        {
            var value = (NativeGuardCase)benchmarkCase.Parameters[nameof(NativeGuardBenchmarks.Case)];

            var wholeTree = value.Keys is NativeGuardKeyKind.NativeNumber or NativeGuardKeyKind.NativeIdentifier || value.Delete;
            var otherNodes = value.Candidates + (wholeTree ? 0 : 1);
            var affectedNodes = value.Requests + (value.Delete ? 1 : 0);

            return new BenchmarkScenario(otherNodes + affectedNodes, BenchmarkShape.Wide,
                value.Requests + 1, value.Candidates);
        }

        if (benchmarkCase.Descriptor.Type == typeof(NativeTrackerWriteBenchmarks))
        {
            var value = (NativeTrackerWriteCase)benchmarkCase.Parameters[nameof(NativeTrackerWriteBenchmarks.Case)];

            return new BenchmarkScenario(1, BenchmarkShape.Wide, 1, value.Tracked);
        }

        if (benchmarkCase.Descriptor.Type == typeof(CleanMutationBoundaryBenchmarks))
        {
            return new BenchmarkScenario(0, BenchmarkShape.Wide, 1,
                (int)benchmarkCase.Parameters[nameof(CleanMutationBoundaryBenchmarks.Tracked)]);
        }

        if (benchmarkCase.Descriptor.Type == typeof(NativeRefreshBenchmarks))
        {
            var value = (NativeRefreshCase)benchmarkCase.Parameters[nameof(NativeRefreshBenchmarks.Case)];

            return new BenchmarkScenario(value.Children + 1, BenchmarkShape.Wide, 1, 0);
        }

        if (benchmarkCase.Descriptor.Type == typeof(AncestorFactReadBenchmarks))
        {
            var value = (NativeFactCase)benchmarkCase.Parameters[nameof(AncestorFactReadBenchmarks.Case)];

            return new BenchmarkScenario(value.Nodes, value.Chain ? BenchmarkShape.Deep : BenchmarkShape.Wide, 1, 0);
        }

        return new BenchmarkScenario(
            (int)benchmarkCase.Parameters[nameof(DatabaseBenchmark.Nodes)],
            (BenchmarkShape)benchmarkCase.Parameters[nameof(DatabaseBenchmark.Shape)],
            (int)benchmarkCase.Parameters[nameof(DatabaseBenchmark.Trees)],
            (int)benchmarkCase.Parameters[nameof(DatabaseBenchmark.Tracked)]);
    }
}
