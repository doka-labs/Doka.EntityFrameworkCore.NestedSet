using System.Data.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

public sealed partial class NullableParentIndexTests
{
    /// <summary>Explains one native prepared query in a fresh fixture with no caller transaction.</summary>
    /// <remarks>Only query execution uses the supplied token; cleanup must survive its cancellation.</remarks>
    private static async Task<GenericPlanResult> ExplainGenericAsync(
        DbContext context,
        string name,
        string query,
        bool scoped,
        string argument,
        CancellationToken cancellationToken
    )
    {
        var connection = context.Database.GetDbConnection();
        var before = await PreparedSessionAsync(context, name);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var prepared = false;
        string plan;
        long generic;
        long custom;
        bool fromSql;

        try
        {
            // WHY: SET LOCAL and rollback preserve the previous session mode even after an aborted query.
            // PREPARE plus its counters proves cached generic planning rather than a fresh parameterized plan.
            command.CommandText = "SET LOCAL plan_cache_mode = force_generic_plan";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            command.CommandText = $"PREPARE {name} ({(scoped ? "integer, integer" : "integer")}) AS {query}";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            prepared = true;
            command.CommandText = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) EXECUTE {name}"
                + $"({(scoped ? "1, " : string.Empty)}{argument})";

            plan = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
            command.CommandText = "SELECT generic_plans, custom_plans, from_sql FROM pg_prepared_statements "
                + "WHERE name = @name";

            AddPreparedName(command, name);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            await reader.ReadAsync(CancellationToken.None);
            generic = reader.GetInt64(0);
            custom = reader.GetInt64(1);
            fromSql = reader.GetBoolean(2);
        }
        finally
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                // WHY: An execution error aborts the owned transaction; DEALLOCATE must run after rollback.
                if (prepared)
                {
                    await PreparedControlAsync(context, $"DEALLOCATE {name}");
                }
            }
        }

        var after = await PreparedSessionAsync(context, name);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            "PostgreSql-generic-" + name,
            [
                "query\n" + query,
                $"generic_plans={generic}\ncustom_plans={custom}\nfrom_sql={fromSql}",
                $"mode_before={before.Mode}\nmode_after={after.Mode}\nowned_statement_count={after.Count}",
                "plan\n" + plan,
            ]);

        return new GenericPlanResult(plan, generic, custom, fromSql, before.Mode, after);
    }

    /// <summary>Creates a private statement identity within PostgreSQL's identifier-length limit.</summary>
    private static string PreparedName() => "nestedset_generic_"
        + Guid
            .NewGuid()
            .ToString("N");

    /// <summary>Reads the effective mode and only the named statement from the same open session.</summary>
    private static async Task<PreparedSession> PreparedSessionAsync(
        DbContext context,
        string name
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT current_setting('plan_cache_mode'), "
            + "(SELECT count(*) FROM pg_prepared_statements WHERE name = @name)";

        AddPreparedName(command, name);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync(CancellationToken.None);

        return new PreparedSession(reader.GetString(0), reader.GetInt64(1));
    }

    /// <summary>Runs test-owned preparation or cleanup without inheriting a canceled query token.</summary>
    private static async Task PreparedControlAsync(
        DbContext context,
        string sql
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>Restores a test's original session setting through a bound value.</summary>
    private static async Task SetSessionModeAsync(
        DbContext context,
        string mode
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT set_config('plan_cache_mode', @mode, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "mode";
        parameter.Value = mode;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(CancellationToken.None);
    }

    /// <summary>Binds the statement identity for catalog observations without SQL string quoting.</summary>
    private static void AddPreparedName(
        DbCommand command,
        string name
    )
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
    }

    /// <summary>Retains the effective session mode and the owned statement count after cleanup.</summary>
    private readonly record struct PreparedSession(
        string Mode,
        long Count
    );

    /// <summary>Retains one executed generic plan, its native cache counters, and restored session state.</summary>
    private sealed record GenericPlanResult(
        string Plan,
        long GenericPlans,
        long CustomPlans,
        bool FromSql,
        string PreviousMode,
        PreparedSession After
    );
}
