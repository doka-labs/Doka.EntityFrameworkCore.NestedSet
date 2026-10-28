namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks diagnostic command ownership and complete plan result handling without a database server.</summary>
public sealed partial class QueryPlanReplayTests
{
    /// <summary>A plan-only watchdog honors explicit context settings without changing ordinary query options.</summary>
    /// <param name="configured">An explicit context timeout, or null for the untouched provider default.</param>
    /// <param name="expected">The deadline applied only to the raw diagnostic command.</param>
    [Theory]
    [InlineData(null, 120)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    public async Task DiagnosticTimeoutDoesNotMutateOrdinaryQueryOptions(
        int? configured,
        int expected
    )
    {
        // Arrange
        await using var connection = new PlanReplayConnection(() => Results(Plan("complete plan")));
        await using var context = CreateContext(connection);
        context.Database.SetCommandTimeout(configured);
        var probe = Probe();

        // Act
        var plan = await QueryPlanTestSupport.ExplainAsync(context, probe, 0, "PostgreSql");

        // Assert
        Assert.Contains("complete plan", plan, StringComparison.Ordinal);
        Assert.NotNull(connection.Command);
        Assert.Equal(expected, connection.Command.CommandTimeout);
        Assert.Equal(configured, context.Database.GetCommandTimeout());
    }

    /// <summary>Successful MySQL replay contains both plan documents but never warning metadata or notes.</summary>
    [Fact]
    public async Task CompleteMySqlReplayExcludesWarningRows()
    {
        // Arrange
        await using var connection = new PlanReplayConnection(() => Results(
            Plan("{\"query_block\":{}}"),
            Plan("-> Filter (actual time=1..2 rows=5 loops=1)"),
            Warnings(1003, "optimizer note")));

        await using var context = CreateContext(connection);
        var probe = Probe();

        // Act
        var plan = await QueryPlanTestSupport.ExplainAsync(context, probe, 0, "MySql");

        // Assert
        Assert.Contains("query_block", plan, StringComparison.Ordinal);
        Assert.Contains("rows=5 loops=1", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("optimizer note", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Warning", plan, StringComparison.Ordinal);
        Assert.NotNull(connection.Command);
        Assert.EndsWith("; SHOW WARNINGS", connection.Command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>Warning 1317 prevents an interrupted TREE document from becoming accepted plan evidence.</summary>
    [Fact]
    public async Task InterruptedMySqlReplayRejectsPartialPlan()
    {
        // Arrange
        await using var connection = new PlanReplayConnection(() => Results(
            Plan("{\"query_block\":{}}"),
            Plan("-> Filter (actual time=1..2 rows=3 loops=1)"),
            Warnings(1317, "Query execution was interrupted")));

        await using var context = CreateContext(connection);
        var probe = Probe();

        // Act
        var error = await Record.ExceptionAsync(
            () => QueryPlanTestSupport.ExplainAsync(context, probe, 0, "MySql"));

        // Assert
        Assert.Contains("1317", Assert.IsType<InvalidOperationException>(error).Message, StringComparison.Ordinal);
    }

    /// <summary>SQL Server's ordinary result values are drained before its separate XML plan is retained.</summary>
    [Fact]
    public async Task SqlServerReplayDrainsDataBeforeXmlPlan()
    {
        // Arrange
        await using var connection = new PlanReplayConnection(() => Results(
            ScalarRows(),
            Plan("<ShowPlanXML />", "Microsoft SQL Server 2005 XML Showplan")));

        await using var context = CreateContext(connection);
        var probe = Probe();

        // Act
        var plan = await QueryPlanTestSupport.ExplainAsync(context, probe, 0, "SqlServer");

        // Assert
        Assert.Equal("<ShowPlanXML />" + Environment.NewLine, plan);
    }

    /// <summary>Creates a small provider graph owned entirely by the test's context.</summary>
    private static DbContext CreateContext(
        PlanReplayConnection connection
    ) => new(new DbContextOptionsBuilder()
        .UseSqlite(connection)
        .EnableServiceProviderCaching(false)
        .Options);

    /// <summary>Supplies an exact recorded statement without creating an unrelated EF command.</summary>
    private static EnterpriseProbe Probe()
    {
        var probe = new EnterpriseProbe();
        probe.Commands.Add("SELECT 1");
        probe.ParameterBindings.Add([]);

        return probe;
    }
}
