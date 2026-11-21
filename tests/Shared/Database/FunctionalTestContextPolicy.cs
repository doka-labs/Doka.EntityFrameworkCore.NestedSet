using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Testing;

/// <summary>Removes implicit SQL and native undo speed limits from case-owned functional test contexts.</summary>
internal static class FunctionalTestContextPolicy
{
    /// <summary>Retains the functional command baseline in immutable options and subsequent fixture clones.</summary>
    /// <param name="options">The existing provider options, with all other configuration retained.</param>
    internal static void ConfigureOptions(
        DbContextOptionsBuilder options
    )
    {
        var relational = RelationalOptionsExtension.Extract(options.Options);

        // WHY: Doka's convenience builder requires a positive timeout. EF's relational option accepts zero,
        // and immutable options must carry it so compiled-model and save-fixture clones retain the baseline.
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(relational.WithCommandTimeout(0));
    }

    /// <summary>Configures an unopened server context or an owned SQLite context with its retained connection.</summary>
    /// <param name="context">The functional fixture context before its first server operation.</param>
    internal static void Configure(
        DbContext context
    )
    {
        var connection = context.Database.GetDbConnection();

        if (connection is SqliteConnection { State: not ConnectionState.Closed } openedSqlite)
        {
            // WHY: Migration fixtures retain an open in-memory SQLite database. Its command default is mutable
            // without changing the connection string or closing the database that owns the schema.
            openedSqlite.DefaultTimeout = 0;
        }
        else if (connection is MySqlConnection mysql)
        {
            var options = new MySqlConnectionStringBuilder(mysql.ConnectionString)
            {
                DefaultCommandTimeout = 0,
            };

            // WHY: Connector-owned transaction commands bypass EF's command setting and use this native default.
            mysql.ConnectionString = options.ConnectionString;
        }
        else if (connection is SqlConnection sqlServer)
        {
            // WHY: Native SqlClient rollback uses the connection timeout rather than EF's command timeout.
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

        // WHY: These tests qualify correctness and bounded work/resources. Their runner owns terminal stopping;
        // deliberate provider-timeout tests may still set their explicit contract after context creation.
        context.Database.SetCommandTimeout(0);
    }
}
