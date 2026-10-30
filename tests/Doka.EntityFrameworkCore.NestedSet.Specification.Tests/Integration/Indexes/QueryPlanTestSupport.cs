using System.Runtime.ExceptionServices;
using System.Text;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Writes query-plan and command evidence for independent shared and provider-local suites.</summary>
internal static class QueryPlanTestSupport
{
    /// <summary>Completes fixture evidence and cleanup without losing either operation's failures.</summary>
    /// <param name="writeEvidence">The class-owned evidence writer, invoked before resource cleanup.</param>
    /// <param name="cleanup">
    /// The cleanup task, including every owned resource when using <c>Task.WhenAll</c>.
    /// </param>
    /// <returns>A completion that retains one original failure or aggregates every operation failure.</returns>
    internal static async ValueTask CompleteFixtureAsync(
        Func<Task> writeEvidence,
        Func<Task> cleanup
    )
    {
        List<Exception>? failures = null;
        await CompleteAsync(writeEvidence);
        await CompleteAsync(cleanup);

        if (failures is null)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo
                .Capture(failures[0])
                .Throw();
        }

        throw new AggregateException("Query-plan fixture evidence and resource cleanup failed.", failures);

        async Task CompleteAsync(
            Func<Task> operation
        )
        {
            Task? pending = null;

            try
            {
                pending = operation();
                await pending;
            }
            catch (Exception failure)
            {
                failures ??= [];

                if (pending?.Exception is { } fault)
                {
                    // WHY: Await exposes one WhenAll failure, while the task retains every owned cleanup failure.
                    // Do not Flatten: one explicitly thrown AggregateException must retain its own identity.
                    failures.AddRange(fault.InnerExceptions);
                }
                else
                {
                    failures.Add(failure);
                }
            }
        }
    }

    /// <summary>
    /// Gives the optimizer current seed distributions without waiting for background statistics collection.
    /// </summary>
    internal static Task RefreshStatisticsAsync<TEntity>(
        DbContext context,
        string engine
    )
        where TEntity : class
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());

        if (engine == "SqlServer")
        {
            return RefreshSqlServerStatisticsAsync(context, sql, table);
        }

        // WHY: Fresh PostgreSQL tables otherwise estimated two scope rows where the probe seeded two hundred.
        // Keep statistics preparation outside measured queries and restrict it to this case's owned table.
        var statement = engine switch
        {
            "MySql" or "MariaDb" => "ANALYZE TABLE " + table,
            _ => "ANALYZE " + table,
        };

        return context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);
    }

    /// <summary>Refreshes every statistic on one owned SQL Server table with its unchanged command watchdog.</summary>
    /// <param name="context">The fixture context whose ordinary provider command settings remain authoritative.</param>
    /// <param name="sql">The configured provider's identifier delimiter.</param>
    /// <param name="table">The fully delimited mapped table, bound as a value when resolving its object identity.</param>
    /// <returns>A task completing after every owned statistic has been refreshed using FULLSCAN.</returns>
    private static async Task RefreshSqlServerStatisticsAsync(
        DbContext context,
        ISqlGenerationHelper sql,
        string table
    )
    {
        // WHY: One all-statistics FULLSCAN accumulated every index/column scan under one command watchdog.
        // Enumerating the same objects preserves exact distributions while bounding each independent refresh.
        var statistics = await context.Database.SqlQueryRaw<string>(
                "SELECT [name] AS [Value] FROM [sys].[stats] "
                + "WHERE [object_id] = OBJECT_ID({0}) ORDER BY [stats_id]", table)
            .ToArrayAsync(CancellationToken.None);

        if (statistics.Length == 0)
        {
            // WHY: Every mapped fixture table has a key statistic. Missing visibility or table identity
            // must fail rather than silently qualifying a table whose distributions were never refreshed.
            throw new InvalidOperationException("No statistics are visible for the owned SQL Server fixture table.");
        }

        foreach (var statistic in statistics)
        {
            // WHY: Only mapped/catalog identifiers enter this statement, and both use the provider delimiter.
            var statement = "UPDATE STATISTICS " + table + " " + sql.DelimitIdentifier(statistic) + " WITH FULLSCAN";
            await context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None);
        }
    }

    /// <summary>
    /// Obtains actual execution plans while retaining the mapped parameter values of the original query.
    /// </summary>
    internal static async Task<string> ExplainAsync(
        DbContext context,
        EnterpriseProbe probe,
        int index,
        string engine
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        // WHY: An instrumented plan returns its first row only after completing the observed query,
        // unlike the streamed read. Bound that separate diagnostic wait without changing EF query options.
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 120;
        var sql = probe.Commands[index];
        command.CommandText = engine switch
        {
            "PostgreSql" => "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql,
            // WHY: Const lookups disappear from MySQL's runtime TREE once fetched during optimization.
            // Retain JSON access-path metadata alongside the actual runtime plan to verify those primary-key reads.
            "MySql" => "EXPLAIN FORMAT=JSON " + sql + "; EXPLAIN ANALYZE FORMAT=TREE " + sql + "; SHOW WARNINGS",
            "MariaDb" => "ANALYZE FORMAT=JSON " + sql,
            "SqlServer" => "SET STATISTICS XML ON; " + sql + "; SET STATISTICS XML OFF;",
            _ => "EXPLAIN QUERY PLAN " + sql,
        };

        foreach (var parameter in probe.ParameterBindings[index])
        {
            command.Parameters.Add(parameter);
        }

        var plan = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        do
        {
            var mySqlWarnings = engine == "MySql"
                && reader.FieldCount == 3
                && reader.GetName(0).Equals("Level", StringComparison.OrdinalIgnoreCase)
                && reader.GetName(1).Equals("Code", StringComparison.OrdinalIgnoreCase)
                && reader.GetName(2).Equals("Message", StringComparison.OrdinalIgnoreCase);

            while (await reader.ReadAsync(CancellationToken.None))
            {
                if (mySqlWarnings)
                {
                    // WHY: MySQL can return a partial TREE after cancellation instead of throwing.
                    // Its warning is decisive even when the plan's counters look structurally valid.
                    if (reader.GetInt32(1) == 1317)
                    {
                        throw new InvalidOperationException(
                            "MySQL query-plan replay was interrupted (warning 1317); partial execution is not complete evidence.");
                    }

                    continue;
                }

                // WHY: SQL Server first returns the actual SELECT results, then a separate XML plan result set.
                if (engine == "SqlServer"
                    && !reader
                        .GetName(0)
                        .Contains("Showplan", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                plan.AppendLine(reader.GetString(engine == "Sqlite" ? 3 : 0));
            }
        } while (await reader.NextResultAsync(CancellationToken.None));

        return plan.ToString();
    }

    /// <summary>
    /// Retains provider plans outside source control for independent inspection and CI artifact collection.
    /// </summary>
    internal static async Task WriteEvidenceAsync(
        string name,
        IReadOnlyList<string> evidence
    )
    {
        var directory = Environment.GetEnvironmentVariable("DOKA_NESTEDSET_QUERY_PLAN_DIR")
            ?? Path.Combine(Path.GetTempPath(), "doka-nestedset-query-plans");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, name + ".txt"),
            string.Join("\n\n", evidence),
            CancellationToken.None);
    }
}
