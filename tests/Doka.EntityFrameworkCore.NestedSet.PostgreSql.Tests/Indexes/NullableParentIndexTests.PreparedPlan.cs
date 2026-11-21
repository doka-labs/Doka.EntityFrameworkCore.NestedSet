using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.ExceptionServices;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

public sealed partial class NullableParentIndexTests
{
    /// <summary>Observes one generic query, with an optional separately labeled index-eligibility control.</summary>
    /// <remarks>All forward work honors the caller; owned cleanup remains protected after cancellation.</remarks>
    private static async Task<GenericPlanResult> ExplainGenericAsync(
        DbContext context,
        string name,
        string query,
        bool scoped,
        string argument,
        bool eligibilityControl,
        CancellationToken cancellationToken
    )
    {
        var connection = context.Database.GetDbConnection();
        var before = await PreparedSessionAsync(context, name, cancellationToken);

        if (before.Count != 0)
        {
            throw new InvalidOperationException("The prepared statement name already belongs to this session.");
        }

        var command = connection.CreateCommand();
        DbTransaction? transaction = null;
        var prepared = false;
        var plan = string.Empty;
        var generic = 0L;
        var custom = 0L;
        var fromSql = false;
        Exception? operationFailure = null;
        List<Exception>? cleanupFailures = null;

        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            command.Transaction = transaction;

            // WHY: SET LOCAL plus protected rollback restores both settings even after an aborted query.
            // Generic counters still prove one actual cached execution, independently of the cost optimizer.
            command.CommandText = "SET LOCAL plan_cache_mode = force_generic_plan";
            await command.ExecuteNonQueryAsync(cancellationToken);

            if (eligibilityControl)
            {
                command.CommandText = "SET LOCAL enable_seqscan = off";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            command.CommandText = $"PREPARE {name} ({(scoped ? "integer, integer" : "integer")}) AS {query}";

            // WHY: Cancellation may arrive after PostgreSQL creates the statement but before its reply is read.
            // Cleanup therefore owns the name before PREPARE and checks actual session existence afterward.
            prepared = true;
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) EXECUTE {name}"
                + $"({(scoped ? "1, " : string.Empty)}{argument})";

            plan = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
            command.CommandText = "SELECT generic_plans, custom_plans, from_sql FROM pg_prepared_statements "
                + "WHERE name = @name";

            AddPreparedName(command, name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            generic = reader.GetInt64(0);
            custom = reader.GetInt64(1);
            fromSql = reader.GetBoolean(2);
        }
        catch (Exception failure)
        {
            operationFailure = failure;
        }
        finally
        {
            try
            {
                await command.DisposeAsync();
            }
            catch (Exception failure)
            {
                (cleanupFailures ??= []).Add(failure);
            }

            if (transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception failure)
                {
                    (cleanupFailures ??= []).Add(failure);
                }

                try
                {
                    await transaction.DisposeAsync();
                }
                catch (Exception failure)
                {
                    (cleanupFailures ??= []).Add(failure);
                }
            }

            if (prepared)
            {
                try
                {
                    await DeallocatePreparedAsync(context, name);
                }
                catch (Exception failure)
                {
                    (cleanupFailures ??= []).Add(failure);
                }
            }
        }

        if (operationFailure is not null)
        {
            if (cleanupFailures is not null)
            {
                cleanupFailures.Insert(0, operationFailure);

                throw new AggregateException("Prepared query execution and cleanup failed.", cleanupFailures);
            }

            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }

        if (cleanupFailures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        }
        else if (cleanupFailures is not null)
        {
            throw new AggregateException("Prepared query cleanup failed.", cleanupFailures);
        }

        var after = await PreparedSessionAsync(context, name, cancellationToken);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            "PostgreSql-generic-" + name,
            [
                "query\n" + query,
                $"eligibility_control={eligibilityControl}",
                $"generic_plans={generic}\ncustom_plans={custom}\nfrom_sql={fromSql}",
                $"mode_before={before.Mode}\nmode_after={after.Mode}\nowned_statement_count={after.Count}",
                $"seqscan_before={before.SequentialScan}\nseqscan_after={after.SequentialScan}",
                "plan\n" + plan,
            ],
            cancellationToken);

        return new GenericPlanResult(plan, generic, custom, fromSql, before, after);
    }

    /// <summary>Releases only the owned statement after rollback, regardless of the operation's token state.</summary>
    private static async Task DeallocatePreparedAsync(
        DbContext context,
        string name
    )
    {
        var retained = await PreparedSessionAsync(context, name, CancellationToken.None);

        if (retained.Count != 0)
        {
            await PreparedControlAsync(context, $"DEALLOCATE {name}", CancellationToken.None);
        }
    }

    /// <summary>Restores the test-owned session mode even when its forward operation was canceled.</summary>
    private static Task RestoreSessionModeAsync(
        DbContext context,
        string mode
    ) => SetSessionModeAsync(context, mode, CancellationToken.None);

    /// <summary>Attempts both owned session cleanup steps and preserves the initiating test failure first.</summary>
    private static async Task CleanupPreparedSessionAsync(
        DbContext context,
        string name,
        string previousMode,
        Exception? operationFailure
    )
    {
        List<Exception>? failures = operationFailure is null ? null : [operationFailure];

        try
        {
            await DeallocatePreparedAsync(context, name);
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        try
        {
            await RestoreSessionModeAsync(context, previousMode);
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        else if (failures is not null)
        {
            throw new AggregateException("Prepared session operation and cleanup failed.", failures);
        }
    }

    /// <summary>Creates a private statement identity within PostgreSQL's identifier-length limit.</summary>
    private static string PreparedName() => "nestedset_generic_"
        + Guid
            .NewGuid()
            .ToString("N");

    /// <summary>Reads effective planner settings and only the named statement from the same open session.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="name">The synthetic object or scenario name.</param>
    private static async Task<PreparedSession> PreparedSessionAsync(
        DbContext context,
        string name,
        CancellationToken cancellationToken
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = "SELECT current_setting('plan_cache_mode'), "
            + "(SELECT count(*) FROM pg_prepared_statements WHERE name = @name), current_setting('enable_seqscan')";

        AddPreparedName(command, name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new PreparedSession(reader.GetString(0), reader.GetInt64(1), reader.GetString(2));
    }

    /// <summary>Runs a forward test-owned statement with the supplied operation token.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="sql">The provider-specific SQL command text.</param>
    private static async Task PreparedControlAsync(
        DbContext context,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Restores a test's original session setting through a bound value.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="mode">The requested transaction or query mode.</param>
    private static async Task SetSessionModeAsync(
        DbContext context,
        string mode,
        CancellationToken cancellationToken
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
        await command.ExecuteScalarAsync(cancellationToken);
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
        long Count,
        string SequentialScan
    );

    /// <summary>Retains one executed generic plan, its native cache counters, and restored session state.</summary>
    private sealed record GenericPlanResult(
        string Plan,
        long GenericPlans,
        long CustomPlans,
        bool FromSql,
        PreparedSession Before,
        PreparedSession After
    );
}
