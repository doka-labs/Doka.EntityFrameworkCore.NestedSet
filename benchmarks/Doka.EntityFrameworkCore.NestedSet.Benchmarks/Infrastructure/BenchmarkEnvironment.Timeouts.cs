namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

public sealed partial class BenchmarkEnvironment
{
    /// <summary>Removes implicit native command and undo deadlines while retaining deliberate overrides.</summary>
    private static DbConnectionStringBuilder ConfigureConnectionTimeoutDefaults(
        BenchmarkEngine engine,
        string connectionString
    )
    {
        DbConnectionStringBuilder connection = engine switch
        {
            BenchmarkEngine.MySql or BenchmarkEngine.MariaDb => new MySqlConnectionStringBuilder(connectionString),
            BenchmarkEngine.PostgreSql => new NpgsqlConnectionStringBuilder(connectionString),
            BenchmarkEngine.SqlServer => new SqlConnectionStringBuilder(connectionString),
            BenchmarkEngine.SqliteMemory or BenchmarkEngine.SqliteFile =>
                new SqliteConnectionStringBuilder(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };

        var keyword = engine switch
        {
            BenchmarkEngine.MySql or BenchmarkEngine.MariaDb => "Default Command Timeout",
            BenchmarkEngine.PostgreSql or BenchmarkEngine.SqlServer => "Command Timeout",
            _ => "Default Timeout",
        };

        // WHY: Native commands bypass EF's command option. ShouldSerialize distinguishes an omitted default
        // from an intentional value, including an explicitly retained provider default such as thirty seconds.
        SetMissingTimeout(connection, keyword);

        if (engine == BenchmarkEngine.SqlServer)
        {
            // WHY: SqlClient native rollback uses the connection deadline rather than an EF command timeout.
            SetMissingTimeout(connection, "Connect Timeout");
        }

        return connection;
    }

    /// <summary>Sets only an omitted provider option instead of replacing an explicitly chosen deadline.</summary>
    private static void SetMissingTimeout(
        DbConnectionStringBuilder connection,
        string keyword
    )
    {
        if (!connection.ShouldSerialize(keyword))
        {
            connection[keyword] = 0;
        }
    }

    /// <summary>Retains unlimited observations in immutable EF options and all subsequent context clones.</summary>
    private static void ConfigureCommandTimeoutDefault(
        DbContextOptionsBuilder builder,
        DbConnectionStringBuilder native
    )
    {
        var relational = RelationalOptionsExtension.Extract(builder.Options);

        if (relational.CommandTimeout is not null)
        {
            return;
        }

        var timeout = native switch
        {
            MySqlConnectionStringBuilder mysql => checked((int)mysql.DefaultCommandTimeout),
            NpgsqlConnectionStringBuilder postgres => postgres.CommandTimeout,
            SqlConnectionStringBuilder sqlServer => sqlServer.CommandTimeout,
            SqliteConnectionStringBuilder sqlite => sqlite.DefaultTimeout,
            _ => throw new InvalidOperationException("The configured benchmark provider has no native timeout policy."),
        };

        // WHY: Doka's convenience builder rejects zero. EF's canonical immutable copy preserves all provider
        // options and accepts the effective native value: unlimited by default, or the caller's explicit choice.
        // SQLite also applies EF's timeout to its connection, so an independent zero would erase that choice.
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(relational.WithCommandTimeout(timeout));
    }
}
