using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Removes implicit SQL and undo latency gates from functional capacity contexts.</summary>
internal static class CapacityContextTimeouts
{
    /// <summary>
    /// Configures only an unopened capacity context; its test runner owns terminal transaction stopping.
    /// </summary>
    /// <param name="context">The case-owned context before its first database operation.</param>
    internal static void Configure(
        DbContext context
    )
    {
        var connection = context.Database.GetDbConnection();
        if (connection is MySqlConnection mysql)
        {
            var options = new MySqlConnectionStringBuilder(mysql.ConnectionString)
            {
                DefaultCommandTimeout = 0,
            };

            // WHY: Connector-created transaction commands bypass EF's command setting and use this default.
            mysql.ConnectionString = options.ConnectionString;
        }
        else if (connection is SqlConnection sqlServer)
        {
            // WHY: Native rollback inherits SqlClient's connection timeout rather than EF's command setting.
            var options = new SqlConnectionStringBuilder(sqlServer.ConnectionString)
            {
                ConnectTimeout = 0,
            };

            sqlServer.ConnectionString = options.ConnectionString;
        }
        else if (connection is NpgsqlConnection postgres)
        {
            var options = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
            {
                CommandTimeout = 0,
            };

            postgres.ConnectionString = options.ConnectionString;
        }
        else if (connection is SqliteConnection sqlite)
        {
            var options = new SqliteConnectionStringBuilder(sqlite.ConnectionString)
            {
                DefaultTimeout = 0,
            };

            sqlite.ConnectionString = options.ConnectionString;
        }

        // WHY: Capacity asserts results and bounded resources, never SQL speed. The outer runner owns stopping.
        context.Database.SetCommandTimeout(0);
    }
}
