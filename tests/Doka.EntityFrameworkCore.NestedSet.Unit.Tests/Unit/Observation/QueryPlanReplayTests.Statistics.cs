namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class QueryPlanReplayTests
{
    /// <summary>Defines the exact ordered FULLSCAN statements without recreating constant array arguments.</summary>
    private static readonly string[] s_statisticsRefreshCommands =
    [
        "UPDATE STATISTICS [fixture]]scope].[Owned]]Statistics] [z-primary] WITH FULLSCAN",
        "UPDATE STATISTICS [fixture]]scope].[Owned]]Statistics] [a-stat]]edge] WITH FULLSCAN",
        "UPDATE STATISTICS [fixture]]scope].[Owned]]Statistics] [middle statistic] WITH FULLSCAN",
    ];

    /// <summary>Each owned statistic retains FULLSCAN and its original context or provider-default watchdog.</summary>
    /// <param name="configured">The context timeout, or null to retain the provider's ordinary default.</param>
    /// <param name="expected">The unchanged deadline expected on the catalog and every update.</param>
    [Theory]
    [InlineData(null, 30)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    public async Task StatisticsRefreshScansEachOwnedStatisticWithUnchangedTimeout(
        int? configured,
        int expected
    )
    {
        // Arrange
        await using var connection = new PlanReplayConnection(
            () => Results(Statistics("z-primary", "a-stat]edge", "middle statistic")), () => 0);

        await using var context = CreateStatisticsContext(connection);
        context.Database.SetCommandTimeout(configured);

        // Act
        await QueryPlanTestSupport.RefreshStatisticsAsync<StatisticsNode>(context, "SqlServer");

        // Assert
        Assert.Equal(4, connection.Commands.Count);
        var catalog = connection.Commands[0];
        Assert.Equal(1, catalog.ReaderExecutions);
        Assert.Equal(0, catalog.NonQueryExecutions);
        Assert.Contains("FROM [sys].[stats]", catalog.CommandText, StringComparison.Ordinal);
        Assert.Contains("WHERE [object_id] = OBJECT_ID(@p0)", catalog.CommandText, StringComparison.Ordinal);
        Assert.EndsWith("ORDER BY [stats_id]", catalog.CommandText.TrimEnd(), StringComparison.Ordinal);
        Assert.Equal("[fixture]]scope].[Owned]]Statistics]", Assert.Single(catalog.ParameterValues));
        Assert.DoesNotContain("Owned", catalog.CommandText, StringComparison.Ordinal);
        Assert.Equal(s_statisticsRefreshCommands, connection.Commands.Skip(1).Select(command => command.CommandText));

        Assert.All(connection.Commands.Skip(1), command => Assert.Equal(1, command.NonQueryExecutions));
        Assert.All(connection.Commands, command => Assert.Equal(expected, command.CommandTimeout));
        Assert.Equal(configured, context.Database.GetCommandTimeout());
    }

    /// <summary>The object identity is a bound value, so a foreign table's catalog cannot supply refresh names.</summary>
    [Fact]
    public async Task StatisticsCatalogBindsOnlyTheOwnedTableIdentity()
    {
        // Arrange
        PlanReplayConnection connection = null!;
        connection = new PlanReplayConnection(() =>
        {
            var identity = Assert.Single(connection.Command!.ParameterValues);

            return Results(Statistics(identity is "[fixture]]scope].[Owned]]Statistics]"
                ? "owned statistic" : "foreign statistic"));
        }, () => 0);

        await using var owned = connection;
        await using var context = CreateStatisticsContext(connection);

        // Act
        await QueryPlanTestSupport.RefreshStatisticsAsync<StatisticsNode>(context, "SqlServer");

        // Assert
        Assert.Equal(2, connection.Commands.Count);
        Assert.Contains("WHERE [object_id] = OBJECT_ID(@p0)", connection.Commands[0].CommandText,
            StringComparison.Ordinal);

        Assert.Equal("UPDATE STATISTICS [fixture]]scope].[Owned]]Statistics] [owned statistic] WITH FULLSCAN",
            connection.Commands[1].CommandText);
    }

    /// <summary>Other engines keep their single existing ANALYZE statement without a catalog or sampling fallback.</summary>
    /// <param name="engine">The existing non-SQL-Server branch.</param>
    /// <param name="prefix">The unchanged provider-specific ANALYZE spelling.</param>
    [Theory]
    [InlineData("MySql", "ANALYZE TABLE ")]
    [InlineData("MariaDb", "ANALYZE TABLE ")]
    [InlineData("PostgreSql", "ANALYZE ")]
    [InlineData("Sqlite", "ANALYZE ")]
    public async Task OtherStatisticsRefreshBranchesRemainSingleStatements(
        string engine,
        string prefix
    )
    {
        // Arrange
        await using var connection = new PlanReplayConnection(
            () => throw new InvalidOperationException("No catalog query is expected."), () => 0);

        await using var context = CreateStatisticsContext(connection);

        // Act
        await QueryPlanTestSupport.RefreshStatisticsAsync<StatisticsNode>(context, engine);

        // Assert
        var command = Assert.Single(connection.Commands);
        Assert.Equal(prefix + "[fixture]]scope].[Owned]]Statistics]", command.CommandText);
        Assert.Equal(1, command.NonQueryExecutions);
        Assert.Equal(0, command.ReaderExecutions);
        Assert.Equal(30, command.CommandTimeout);
        Assert.Null(context.Database.GetCommandTimeout());
    }

    /// <summary>Returns the single nonnullable column consumed by EF's scalar catalog query.</summary>
    private static DataTable Statistics(
        params string[] names
    )
    {
        var table = new DataTable();
        table.Columns.Add("Value", typeof(string));

        foreach (var name in names)
        {
            table.Rows.Add(name);
        }

        return table;
    }

    /// <summary>Uses SQL Server's actual identifier delimiter while substituting only raw command execution.</summary>
    private static StatisticsContext CreateStatisticsContext(
        PlanReplayConnection connection
    ) => new(new DbContextOptionsBuilder<StatisticsContext>()
        .UseSqlServer(connection)
        .EnableServiceProviderCaching(false)
        .Options);

    /// <summary>Maps an explicitly owned table with identifiers that require provider escaping.</summary>
    private sealed class StatisticsContext(DbContextOptions<StatisticsContext> options) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<StatisticsNode>().ToTable("Owned]Statistics", "fixture]scope");
        }
    }

    /// <summary>Supplies the conventional key statistic expected for every mapped fixture table.</summary>
    private sealed class StatisticsNode
    {
        /// <summary>Gets or sets the test row identity.</summary>
        public int Id { get; set; }
    }
}
