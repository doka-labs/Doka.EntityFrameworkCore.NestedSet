namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class QueryPlanReplayTests
{
    /// <summary>A failed catalog command cannot silently fall back to all-table or sampled updates.</summary>
    [Fact]
    public async Task StatisticsCatalogFailurePropagatesWithoutUpdates()
    {
        // Arrange
        var failure = new InvalidOperationException("Catalog lookup failed.");
        await using var connection = new PlanReplayConnection(() => throw failure, () => 0);
        await using var context = CreateStatisticsContext(connection);

        // Act
        var error = await Record.ExceptionAsync(
            () => QueryPlanTestSupport.RefreshStatisticsAsync<StatisticsNode>(context, "SqlServer"));

        // Assert
        Assert.Same(failure, error);
        var command = Assert.Single(connection.Commands);
        Assert.Equal(1, command.ReaderExecutions);
        Assert.Equal(0, command.NonQueryExecutions);
    }

    /// <summary>A failed catalog reader prevents every update instead of accepting incomplete names.</summary>
    [Fact]
    public async Task StatisticsCatalogReaderFailurePreventsUpdates()
    {
        // Arrange
        await using var connection = new PlanReplayConnection(() =>
        {
            var reader = Results(Statistics("unread statistic"));
            reader.Close();

            return reader;
        }, () => 0);

        await using var context = CreateStatisticsContext(connection);

        // Act
        var error = await Record.ExceptionAsync(
            () => QueryPlanTestSupport.RefreshStatisticsAsync<StatisticsNode>(context, "SqlServer"));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        var command = Assert.Single(connection.Commands);
        Assert.Equal(1, command.ReaderExecutions);
        Assert.Equal(0, command.NonQueryExecutions);
    }

    /// <summary>Missing or invisible keyed-table statistics cannot become a successful no-op qualification.</summary>
    [Fact]
    public async Task EmptyStatisticsCatalogFailsClosed()
    {
        // Arrange
        await using var connection = new PlanReplayConnection(() => Results(Statistics()), () => 0);
        await using var context = CreateStatisticsContext(connection);

        // Act
        var error = await Record.ExceptionAsync(
            () => QueryPlanTestSupport.RefreshStatisticsAsync<StatisticsNode>(context, "SqlServer"));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        var command = Assert.Single(connection.Commands);
        Assert.Equal(1, command.ReaderExecutions);
        Assert.Equal(0, command.NonQueryExecutions);
    }

    /// <summary>A failed statistic update remains the original failure and prevents later statistics from running.</summary>
    [Fact]
    public async Task StatisticsUpdateFailureStopsBeforeLaterStatistics()
    {
        // Arrange
        var failure = new InvalidOperationException("Statistic update failed.");
        var updates = 0;
        await using var connection = new PlanReplayConnection(
            () => Results(Statistics("first", "failing", "unreached")),
            () => ++updates == 2 ? throw failure : 0);

        await using var context = CreateStatisticsContext(connection);

        // Act
        var error = await Record.ExceptionAsync(
            () => QueryPlanTestSupport.RefreshStatisticsAsync<StatisticsNode>(context, "SqlServer"));

        // Assert
        Assert.Same(failure, error);
        Assert.Equal(2, updates);
        Assert.Equal(3, connection.Commands.Count);
        Assert.Equal("UPDATE STATISTICS [fixture]]scope].[Owned]]Statistics] [failing] WITH FULLSCAN",
            connection.Commands[2].CommandText);

        Assert.DoesNotContain(connection.Commands, command => command.CommandText.Contains("unreached", StringComparison.Ordinal));
    }
}
