namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares fixed query-build allocation measurements without trimming or favorable-sample retries.</summary>
internal static class QueryConstructionAllocation
{
    /// <summary>Returns the mean current-thread allocation across exactly 100 complete synchronous builds.</summary>
    /// <param name="build">The prepared builder delegate; callers warm its query shape before measurement.</param>
    /// <returns>The mean managed bytes per complete build, including every measured invocation.</returns>
    internal static long Measure(
        Func<object> build
    )
    {
        ArgumentNullException.ThrowIfNull(build);
        const int operations = 100;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < operations; index++)
        {
            var result = build();
            GC.KeepAlive(result);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / operations;
    }

    /// <summary>Returns both mean allocations across exactly 1000 adjacent, alternating build pairs.</summary>
    /// <param name="baseline">The prepared, warmed baseline builder delegate.</param>
    /// <param name="expanded">The prepared, warmed builder with one additional scalar criterion.</param>
    /// <returns>Mean managed bytes per complete build for each variant; every sample contributes.</returns>
    internal static (long Baseline, long Expanded) MeasurePaired(
        Func<object> baseline,
        Func<object> expanded
    )
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(expanded);
        const int pairs = 1000;
        long baselineBytes = 0;
        long expandedBytes = 0;

        // WHY: Adjacent ABBA pairs share cache and runtime phases without assigning either variant a separate window.
        // A fixed mean retains occasional allocation costs instead of trimming samples or retrying until budgets pass.
        for (var index = 0; index < pairs; index++)
        {
            if ((index & 1) == 0)
            {
                baselineBytes += MeasureOne(baseline);
                expandedBytes += MeasureOne(expanded);
            }
            else
            {
                expandedBytes += MeasureOne(expanded);
                baselineBytes += MeasureOne(baseline);
            }
        }

        return (baselineBytes / pairs, expandedBytes / pairs);
    }

    /// <summary>Retains one returned query while counting only its complete synchronous construction.</summary>
    private static long MeasureOne(
        Func<object> build
    )
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = build();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(result);

        return allocated;
    }
}
