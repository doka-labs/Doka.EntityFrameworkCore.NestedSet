namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards cumulative native PostgreSQL work without relying on elapsed time or row estimates.</summary>
public sealed class CompositionPostgreSqlPlanWorkTests
{
    /// <summary>The observed quadratic join cannot pass merely because its final result is small.</summary>
    [Fact]
    public void RejectedCartesianJoinWorkExceedsTheOriginalLinearBudget()
    {
        // Arrange
        const long budget = 320_448;
        const string plan = """
            [{"Plan":{"Actual Rows":1000,"Actual Loops":1,"Rows Removed by Join Filter":1999000,
                "Plans":[{"Actual Rows":2000,"Actual Loops":1000}]}}]
            """;

        // Act
        var actual = CompositionPlanWork.ReadPostgreSqlJson(plan);

        // Assert
        Assert.Equal(1_000D, actual.RootOutputTuples);
        Assert.Equal(2_000_000D, actual.MaximumOperatorTuples);
        Assert.Equal(1_999_000D, actual.MaximumRejectedJoinTuples);
        Assert.True(actual.MaximumOperatorTuples > budget);
        Assert.True(actual.MaximumRejectedJoinTuples > budget);
    }

    /// <summary>Fractional per-loop observations are multiplied; absent rejection counters mean zero.</summary>
    [Fact]
    public void AveragedObservationsAndExecutedLoopsAreCountedExactly()
    {
        // Arrange
        const string plan = """
            [{"Plan":{"Actual Rows":5,"Actual Loops":1,"Plan Rows":1e20,
                "Plans":[{"Actual Rows":2.5,"Actual Loops":4,"Rows Removed by Join Filter":1.25},
                    {"Actual Rows":1e20,"Actual Loops":0}]},
                "JIT":{"Actual Rows":1e20,"Actual Loops":1e20}}]
            """;

        // Act
        var actual = CompositionPlanWork.ReadPostgreSqlJson(plan);

        // Assert
        Assert.Equal(new CompositionPostgreSqlPlanWork(10, 5, 2, 5), actual);
    }

    /// <summary>Empty actual output is valid when the root executed and has no rejected join tuples.</summary>
    [Fact]
    public void AnExecutedEmptyResultDoesNotBecomeMissingEvidence()
    {
        // Arrange
        const string plan = """[{"Plan":{"Actual Rows":0,"Actual Loops":1}}]""";

        // Act
        var actual = CompositionPlanWork.ReadPostgreSqlJson(plan);

        // Assert
        Assert.Equal(new CompositionPostgreSqlPlanWork(0, 0, 1, 0), actual);
    }

    /// <summary>
    /// Malformed, absent, null, negative, or nonfinite observations cannot silently satisfy a work gate.
    /// </summary>
    /// <param name="plan">Invalid or nonexecuted native evidence.</param>
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[{\"Plan\":null}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":1}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":null,\"Actual Loops\":1}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":1,\"Actual Loops\":-1}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":1e400,\"Actual Loops\":1}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":1e308,\"Actual Loops\":1e308}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":0,\"Actual Loops\":0}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":1,\"Actual Loops\":1,\"Rows Removed by Join Filter\":null}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":1,\"Actual Loops\":1,\"Rows Removed by Join Filter\":-1}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":1,\"Actual Loops\":1,\"Plans\":null}}]")]
    [InlineData("[{\"Plan\":{\"Actual Rows\":1,\"Actual Loops\":1,\"Plans\":[null]}}]")]
    public void MissingOrMalformedNativeWorkIsRejected(string plan)
    {
        // Arrange
        var act = () =>
        {
            _ = CompositionPlanWork.ReadPostgreSqlJson(plan);
        };

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.IsType<FormatException>(exception);
    }
}
