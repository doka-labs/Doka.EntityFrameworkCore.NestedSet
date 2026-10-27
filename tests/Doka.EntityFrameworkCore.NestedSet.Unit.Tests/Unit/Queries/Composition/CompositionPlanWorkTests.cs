namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Proves native-work limits reject both observed expensive strategies without timing assertions.</summary>
public sealed class CompositionPlanWorkTests
{
    /// <summary>Rejects the actual selective UNION population materialization from the ten-million fixture.</summary>
    [Fact]
    public void SelectivePopulationMaterializationExceedsIndependentPairBudget()
    {
        // Arrange
        const string plan = "-> Union materialize with deduplication (cost=437246 rows=33653) "
            + "(actual time=27523..27523 rows=10e+6 loops=1)";

        var budget = CompositionPlanWorkBudget.ForPairs(10_000_000, 1, 100, 100);

        // Act
        var actual = CompositionPlanWork.ReadMySqlTree(plan);

        // Assert
        Assert.Equal(10_000_000D, actual.MaximumMaterializedTuples);
        Assert.True(actual.MaximumMaterializedTuples > budget.MaterializedTuples);
    }

    /// <summary>Rejects the actual broad range join's hundred-million candidate amplification.</summary>
    [Fact]
    public void BroadCartesianCandidateWorkExceedsIndependentPairBudget()
    {
        // Arrange
        const string plan = "-> Inner hash join (no condition) (cost=1.13e+6 rows=0.0418) "
            + "(actual time=296..8987 rows=100e+6 loops=1)";

        var budget = CompositionPlanWorkBudget.ForPairs(100_000, 1_000, 100_000, 100_999);

        // Act
        var actual = CompositionPlanWork.ReadMySqlTree(plan);

        // Assert
        Assert.Equal(100_000_000D, actual.MaximumOperatorTuples);
        Assert.True(actual.MaximumOperatorTuples > budget.OperatorTuples);
    }

    /// <summary>Accepts legitimate selective pair work and one broad materialized output bag.</summary>
    [Theory]
    [InlineData(1, 100, 100, 100)]
    [InlineData(1_000, 100_000, 100_999, 100_999)]
    public void BoundedRequestedWorkFitsIndependentPairBudget(
        int anchors,
        int facts,
        long contributions,
        long tuples
    )
    {
        // Arrange
        var plan = $"-> Union materialize (actual time=1..2 rows={tuples} loops=1)";
        var budget = CompositionPlanWorkBudget.ForPairs(100_000, anchors, facts, contributions);

        // Act
        var actual = CompositionPlanWork.ReadMySqlTree(plan);

        // Assert
        Assert.InRange(actual.MaximumOperatorTuples, 0, budget.OperatorTuples);
        Assert.InRange(actual.MaximumMaterializedTuples, 0, budget.MaterializedTuples);
    }

    /// <summary>Counts repeated invalid-set scans rather than only its once-built population.</summary>
    [Fact]
    public void DenseInvalidSetRescansExceedSelectedPathEvidenceBudget()
    {
        // Arrange
        const string plan = "-> Table scan on bad (actual time=1..2 rows=50000 loops=100)\n"
            + "    -> Materialize (actual time=1..2 rows=50000 loops=1)";

        var budget = CompositionPlanWorkBudget.ForPaths(100_000, 100, 100_000);

        // Act
        var actual = CompositionPlanWork.ReadMySqlTree(plan);

        // Assert
        Assert.Equal(5_000_000D, actual.MaximumOperatorTuples);
        Assert.Equal(50_000D, actual.MaximumMaterializedTuples);
        Assert.True(actual.MaximumOperatorTuples > budget.OperatorTuples);
    }

    /// <summary>Uses actual averaged rows and ignores estimates and iterators that never ran.</summary>
    [Fact]
    public void ActualFractionalRowsAreMultipliedByLoops()
    {
        // Arrange
        const string plan = "-> Lookup (cost=1e+20 rows=1e+20) (actual time=1..2 rows=0.25 loops=40)\n"
            + "-> Materialize (cost=1e+20 rows=1e+20) (never executed)";

        // Act
        var actual = CompositionPlanWork.ReadMySqlTree(plan);

        // Assert
        Assert.Equal(new CompositionPlanWork(10, 0, 1, 10, 10), actual);
    }

    /// <summary>Invalidation signals do not misreport their driving rows as newly materialized tuples.</summary>
    [Fact]
    public void InvalidationIteratorIsNotCountedAsMaterialization()
    {
        // Arrange
        const string plan = "-> Invalidate materialized tables (row from t) "
            + "(actual time=1..2 rows=1000 loops=1)\n"
            + "-> Union materialize with deduplication "
            + "(actual time=1..2 rows=1 loops=100)";

        // Act
        var actual = CompositionPlanWork.ReadMySqlTree(plan);

        // Assert
        Assert.Equal(new CompositionPlanWork(1_000, 100, 2, 1_000, 1_100), actual);
    }

    /// <summary>All executed iterator outputs contribute their displayed average rows multiplied by loops.</summary>
    [Fact]
    public void IteratorOutputSumIncludesEveryExecutedStageAndItsLoops()
    {
        // Arrange
        const string plan = """
            -> Root (actual time=1..2 rows=2 loops=1)
                -> Lookup (cost=1e20 rows=1e20) (actual time=1..2 rows=0.25 loops=40)
                    -> Materialize (actual time=1..2 rows=3 loops=2)
                    -> Unused (cost=1e20 rows=1e20) (never executed)
            """;

        // Act
        var actual = CompositionPlanWork.ReadMySqlTree(plan);

        // Assert
        Assert.Equal(new CompositionPlanWork(10, 6, 3, 2, 18), actual);
    }

    /// <summary>
    /// Individually finite observations cannot silently produce an infinite aggregate allowance check.
    /// </summary>
    [Fact]
    public void FiniteIteratorOutputsCannotOverflowTheirAggregate()
    {
        // Arrange
        const string plan = """
            -> Root (actual time=1..2 rows=1e308 loops=1)
                -> Child (actual time=1..2 rows=1e308 loops=1)
            """;

        // Act
        var error = Record.Exception(() => CompositionPlanWork.ReadMySqlTree(plan));

        // Assert
        var malformed = Assert.IsType<FormatException>(error);
        Assert.Contains("sum", malformed.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A partial replay cannot be accepted merely because some child iterators emitted rows.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1_000, true)]
    public void ReplayCompletionUsesRootOutputInsteadOfLargestChildOutput(
        int rootRows,
        bool completed
    )
    {
        // Arrange
        const int actualResultRows = 1_000;
        var plan = $"-> Sort (actual time=1..2 rows={rootRows} loops=1)\n"
            + "    -> Scan (actual time=1..2 rows=1000 loops=1)";

        // Act
        var actual = CompositionPlanWork.ReadMySqlTree(plan);

        // Assert
        Assert.Equal(1_000D, actual.MaximumOperatorTuples);
        Assert.Equal(completed, actual.RootOutputTuples == actualResultRows);
    }

    /// <summary>Rejects repeated full-tree prefixes even when ICP conceals rejected index entries.</summary>
    [Fact]
    public void RepeatedScopeOnlyLookupsExposeSearchDomainAmplification()
    {
        // Arrange
        const string plan = "-> Index lookup on t0 using IX_NestedSet_123 (Tree=1, TreeId=0x00), "
            + "with index condition: (t0.NodeId = t.NodeId) "
            + "(actual time=9.73..9.8 rows=101 loops=1000)";

        var budget = CompositionPlanWorkBudget.ForPairs(100_000, 1_000, 100_000, 100_999);

        // Act
        var loops = CompositionPlanWork.FullTreePrefixLoops(plan);

        // Assert
        Assert.Equal(1_000D, loops);
        Assert.True(loops * 100_000 > budget.OperatorTuples);
    }

    /// <summary>Permits finite range access, keyed lookup, and a once-read whole-tree prefix.</summary>
    [Fact]
    public void FiniteAndOnceReadAccessPathsAreNotRejectedAsRepeatedFullPrefixes()
    {
        // Arrange
        const string plan = "-> Index range scan on t using IX_NestedSet_123 "
            + "over (Tree = 1 AND TreeId = 0x00 AND 999 <= Start <= 1000) "
            + "(actual time=1..2 rows=1 loops=1000)\n"
            + "-> Index lookup on t using IX_NestedSet_456 (Tree=1, TreeId=0x00, NodeId=x.Id) "
            + "(actual time=1..2 rows=1 loops=1000)\n"
            + "-> Index lookup on t using IX_NestedSet_789 (Tree=1, TreeId=0x00) "
            + "(actual time=1..2 rows=100000 loops=1)";

        // Act
        var loops = CompositionPlanWork.FullTreePrefixLoops(plan);

        // Assert
        Assert.Equal(1D, loops);
    }

    /// <summary>Scope-only range access cannot masquerade as a finite coordinate seek through ICP.</summary>
    [Fact]
    public void EqualityOnlyRangeWithIndexConditionStillExposesWholePrefixDomain()
    {
        // Arrange
        const string plan = "-> Index range scan on t2 using IX_NestedSet_123 "
            + "over (Tree = 1 AND TreeId = 0x00), with index condition: (t2.Start > t.Start) "
            + "(actual time=10.3..10.9 rows=990 loops=100)";

        var budget = CompositionPlanWorkBudget.ForPaths(100_000, 100, 99_000);

        // Act
        var loops = CompositionPlanWork.FullTreePrefixLoops(plan);

        // Assert
        Assert.Equal(100D, loops);
        Assert.True(loops * 100_000 > budget.OperatorTuples);
    }

    /// <summary>Missing actual evidence cannot silently satisfy the work gate.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{\"rows_examined_per_scan\": 10000000}")]
    [InlineData("-> Materialize (cost=1 rows=100) (never executed)")]
    [InlineData("-> Scan (actual time=1..2 rows=NaN loops=1)")]
    [InlineData("-> Scan (actual time=1..2 rows=100)")]
    public void MissingOrMalformedRuntimeEvidenceIsRejected(
        string plan
    )
    {
        // Arrange
        var act = () =>
        {
            _ = CompositionPlanWork.ReadMySqlTree(plan);
        };

        // Act
        var error = Record.Exception(act);

        // Assert
        Assert.IsType<FormatException>(error);
    }
}
