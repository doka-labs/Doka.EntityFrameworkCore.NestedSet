using MySqlConnector;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Checks complete and interrupted diagnostic plans against the actual MySQL server.</summary>
public sealed class QueryPlanReplayTests : ProviderTest,
    IClassFixture<ProviderFixture<RelationalFixture, MySqlEngine>>
{
    /// <summary>Uses the MySQL-owned fixture while each case retains its own database and connection.</summary>
    /// <param name="fixture">The provider-owned relational resource.</param>
    public QueryPlanReplayTests(
        ProviderFixture<RelationalFixture, MySqlEngine> fixture
    ) : base(fixture) { }

    /// <summary>Successful instrumented execution retains JSON and actual TREE evidence without changing EF options.</summary>
    [Fact]
    public async Task CompletedReplayRetainsActualExecutionEvidence()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var context = database.CreateContext();
        var originalTimeout = context.Database.GetCommandTimeout();
        var probe = Probe("SELECT SLEEP(0.01) AS replay_value");

        // Act
        var plan = await QueryPlanTestSupport.ExplainAsync(context, probe, 0, Engine);

        // Assert
        Assert.Contains("query_block", plan, StringComparison.Ordinal);
        Assert.Contains("actual time=", plan, StringComparison.Ordinal);
        Assert.Equal(originalTimeout, context.Database.GetCommandTimeout());
    }

    /// <summary>An explicit short observation budget cannot turn interrupted TREE output into complete evidence.</summary>
    [Fact]
    public async Task InterruptedReplayCannotReturnPartialExecutionEvidence()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var context = database.CreateContext();
        context.Database.SetCommandTimeout(1);
        var probe = Probe("SELECT SLEEP(2) AS replay_value");

        // Act
        var error = await Record.ExceptionAsync(
            () => QueryPlanTestSupport.ExplainAsync(context, probe, 0, Engine));

        // Assert
        // WHY: Drivers may reject the interrupted command directly. If MySQL instead returns a
        // partial TREE with warning 1317, the helper must reject it rather than accepting that plan.
        Assert.True(error is InvalidOperationException observation
                && observation.Message.Contains("1317", StringComparison.Ordinal)
            || error is MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired or MySqlErrorCode.QueryInterrupted },
            error?.ToString() ?? "The interrupted replay returned partial plan evidence as a success.");

        Assert.Equal(1, context.Database.GetCommandTimeout());
    }

    /// <summary>Records a native scalar statement without involving another query's command timeout.</summary>
    private static EnterpriseProbe Probe(
        string sql
    )
    {
        var probe = new EnterpriseProbe();
        probe.Commands.Add(sql);
        probe.ParameterBindings.Add([]);

        return probe;
    }
}
