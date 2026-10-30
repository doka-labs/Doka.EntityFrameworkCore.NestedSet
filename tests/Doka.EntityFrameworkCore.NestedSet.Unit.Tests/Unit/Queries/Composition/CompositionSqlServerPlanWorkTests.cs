namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards native SQL Server work independently of timing, estimates and final result size.</summary>
public sealed class CompositionSqlServerPlanWorkTests
{
    /// <summary>Residual filtering cannot conceal the population repeatedly scanned by the old long-path plan.</summary>
    [Fact]
    public void RepeatedReadsExceedTheUnchangedPathBudget()
    {
        // Arrange
        var plan = Plan("""
            <RunTimeCountersPerThread Thread="0" ActualRows="95050" ActualRowsRead="10000100" ActualExecutions="100" />
            """);
        var budget = CompositionPlanWorkBudget.ForPaths(100_001, 100, 95_050);

        // Act
        var work = CompositionSqlServerPlanWork.ReadXml(plan);

        // Assert
        Assert.Equal(95_050, work.MaximumOperatorTuples);
        Assert.Equal(10_000_100, work.MaximumOperatorRowsRead);
        Assert.True(work.MaximumOperatorTuples < budget.OperatorTuples);
        Assert.True(work.MaximumOperatorRowsRead > budget.OperatorTuples);
    }

    /// <summary>Thread counters are summed once; SQL Server already includes every execution in each value.</summary>
    [Fact]
    public void ParallelCumulativeRowsAreNotMultipliedByExecutions()
    {
        // Arrange
        var plan = Plan("""
            <RunTimeCountersPerThread Thread="1" ActualRows="4" ActualRowsRead="9" ActualExecutions="10" />
            <RunTimeCountersPerThread Thread="2" ActualRows="6" ActualRowsRead="11" ActualExecutions="20" />
            """);

        // Act
        var work = CompositionSqlServerPlanWork.ReadXml(plan);

        // Assert
        Assert.Equal(new CompositionSqlServerPlanWork(10, 20, 1), work);
    }

    /// <summary>An executed empty seek may legitimately omit the pre-filter read counter.</summary>
    [Fact]
    public void EmptyExecutedSeekIsValidEvidence()
    {
        // Arrange
        var plan = Plan("""<RunTimeCountersPerThread Thread="0" ActualRows="0" ActualExecutions="1" />""");

        // Act
        var work = CompositionSqlServerPlanWork.ReadXml(plan);

        // Assert
        Assert.Equal(new CompositionSqlServerPlanWork(0, 0, 1), work);
    }

    /// <summary>A positive storage read cannot silently lose its required pre-filter work observation.</summary>
    [Fact]
    public void MissingReadCounterForPositiveStorageOutputIsRejected()
    {
        // Arrange
        var plan = Plan("""<RunTimeCountersPerThread ActualRows="1" ActualExecutions="1" />""")
            .Replace("<RelOp ", "<RelOp PhysicalOp=\"Clustered Index Scan\" ", StringComparison.Ordinal);

        // Act
        var error = Record.Exception(() => CompositionSqlServerPlanWork.ReadXml(plan));

        // Assert
        var failure = Assert.IsType<FormatException>(error);
        Assert.Contains("ActualRowsRead", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The actual budget guard rejects hidden reads as well as excessive emitted tuples.</summary>
    /// <param name="output">The cumulative emitted work.</param>
    /// <param name="read">The cumulative work before residual filtering.</param>
    /// <param name="accepted">Whether both observations satisfy the independently derived allowance.</param>
    [Theory]
    [InlineData(95_050, 100_001, true)]
    [InlineData(95_050, 10_000_100, false)]
    [InlineData(10_000_100, 100_001, false)]
    public void ActualBudgetGuardChecksBothCounters(long output, long read, bool accepted)
    {
        // Arrange
        var plan = Plan(FormattableString.Invariant(
            $"<RunTimeCountersPerThread ActualRows=\"{output}\" ActualRowsRead=\"{read}\" ActualExecutions=\"100\" />"));

        var budget = CompositionPlanWorkBudget.ForPaths(100_001, 100, 95_050);

        // Act
        var error = Record.Exception(() => CompositionSqlServerPlanWork.AssertWithinBudget(plan, budget, "long-path"));

        // Assert
        if (accepted)
        {
            Assert.Null(error);
        }
        else
        {
            var failure = Assert.IsType<TrueException>(error);
            Assert.Contains("long-path", failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Missing, estimated-only, invalid or nonexecuted observations cannot pass the work guard.</summary>
    /// <param name="counters">Malformed or nonexecuted runtime counters.</param>
    [Theory]
    [InlineData("")]
    [InlineData("<RunTimeCountersPerThread ActualRows=\"0\" ActualExecutions=\"0\" />")]
    [InlineData("<RunTimeCountersPerThread ActualRows=\"1\" />")]
    [InlineData("<RunTimeCountersPerThread ActualExecutions=\"1\" />")]
    [InlineData("<RunTimeCountersPerThread ActualRows=\"-1\" ActualExecutions=\"1\" />")]
    [InlineData("<RunTimeCountersPerThread ActualRows=\"1\" ActualExecutions=\"-1\" />")]
    [InlineData("<RunTimeCountersPerThread ActualRows=\"1\" ActualExecutions=\"1\" ActualRowsRead=\"NaN\" />")]
    [InlineData("<RunTimeCountersPerThread ActualRows=\"1e400\" ActualExecutions=\"1\" />")]
    public void InvalidRuntimeEvidenceIsRejected(string counters)
    {
        // Arrange
        var plan = Plan(counters);

        // Act
        var exception = Record.Exception(() => CompositionSqlServerPlanWork.ReadXml(plan));

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Wrong roots and external entities cannot become apparent execution evidence.</summary>
    /// <param name="plan">A non-Showplan or forbidden XML document.</param>
    [Theory]
    [InlineData("")]
    [InlineData("<root />")]
    [InlineData("<!DOCTYPE root [<!ENTITY value SYSTEM 'file:///unread'>]><root>&value;</root>")]
    public void InvalidXmlEvidenceIsRejected(string plan)
    {
        // Arrange
        Action read = () =>
        {
            _ = CompositionSqlServerPlanWork.ReadXml(plan);
        };

        // Act
        var exception = Record.Exception(read);

        // Assert
        Assert.IsType<FormatException>(exception);
    }

    /// <summary>Builds a minimal actual-plan envelope without retaining a production or hosted query.</summary>
    private static string Plan(string counters) =>
        "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\">"
        + "<RelOp EstimateRows=\"1e20\"><RunTimeInformation>" + counters
        + "</RunTimeInformation></RelOp></ShowPlanXML>";
}
