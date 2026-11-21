namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Derives conservative tuple-work limits from independent fixture cardinalities.</summary>
/// <param name="OperatorTuples">The maximum emitted tuples permitted at any executed iterator.</param>
/// <param name="MaterializedTuples">The maximum materialized tuples permitted at any iterator.</param>
internal readonly record struct CompositionPlanWorkBudget(
    long OperatorTuples,
    long MaterializedTuples
)
{
    /// <summary>Allows either selective candidate work or one population scan plus the requested pair bag.</summary>
    internal static CompositionPlanWorkBudget ForPairs(
        int nodes,
        int anchors,
        int facts,
        long contributions
    )
    {
        // WHY: The factor two tolerates rounded native observations and equivalent legitimate access paths,
        // while separating requested output from a whole-population materialization for a selective payload.
        var candidateWork = Math.Min((long)anchors * facts, nodes + contributions);

        return new CompositionPlanWorkBudget(
            checked(2 * (anchors + (long)facts + candidateWork)),
            checked(2 * (anchors + (long)facts + contributions)));
    }

    /// <summary>Allows one population validation plus the independently required selected path evidence.</summary>
    internal static CompositionPlanWorkBudget ForPaths(
        int nodes,
        int occurrences,
        long requiredEdges
    ) => new(
        checked(2 * (nodes + (long)occurrences + requiredEdges)),
        checked(2L * nodes));
}
