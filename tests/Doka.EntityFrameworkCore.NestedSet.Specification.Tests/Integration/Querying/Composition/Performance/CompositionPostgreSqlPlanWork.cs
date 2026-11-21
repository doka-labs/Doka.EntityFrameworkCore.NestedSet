namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Summarizes emitted tuples and tuples rejected by an executed PostgreSQL join predicate.</summary>
/// <param name="MaximumOperatorTuples">The largest actual rows multiplied by actual loops.</param>
/// <param name="MaximumRejectedJoinTuples">The largest join-filter rejections multiplied by actual loops.</param>
/// <param name="ExecutedOperators">The number of operators that ran at least once.</param>
/// <param name="RootOutputTuples">The complete statement's output used to check replay completion.</param>
internal readonly record struct CompositionPostgreSqlPlanWork(
    double MaximumOperatorTuples,
    double MaximumRejectedJoinTuples,
    int ExecutedOperators,
    double RootOutputTuples
);
