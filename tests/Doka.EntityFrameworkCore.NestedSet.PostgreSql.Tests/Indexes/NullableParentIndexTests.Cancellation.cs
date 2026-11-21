using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Npgsql;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

public sealed partial class NullableParentIndexTests
{
    /// <summary>
    /// Canceling a genuinely executing prepared query cannot cancel its statement or session cleanup.
    /// </summary>
    [Fact]
    public async Task ExplainGenericAsync_ExecutionCancellation_CleansStatementAndRestoresSettings()
    {
        // Arrange
        var runnerToken = TestContext.Current.CancellationToken;
        var database = await _fixture.ResetAsync(Engine, runnerToken);
        await using var context = database.CreateContext();
        var subject = (NpgsqlConnection)context.Database.GetDbConnection();

        // WHY: Npgsql documents zero as an unbounded cancellation-response wait. This case exercises
        // healthy-session cleanup, so host load must not turn its default two-second fallback into a gate.
        var subjectOptions = new NpgsqlConnectionStringBuilder(subject.ConnectionString)
        {
            CancellationTimeout = 0,
        };

        subject.ConnectionString = subjectOptions.ConnectionString;
        await context.Database.OpenConnectionAsync(runnerToken);
        var name = PreparedName();
        var original = await PreparedSessionAsync(context, name, runnerToken);
        var originalSequentialScan = await ReadSequentialScanSettingAsync(context, runnerToken);
        await SetSessionModeAsync(context, "force_custom_plan", runnerToken);

        // WHY: The unpooled owner releases its session advisory lock on disposal even after a failed probe.
        var blockerOptions = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString())
        {
            Pooling = false,
            CommandTimeout = 0,
        };

        await using var blocker = new NpgsqlConnection(blockerOptions.ConnectionString);
        await blocker.OpenAsync(runnerToken);
        await using var acquire = blocker.CreateCommand();
        const int lockKey = 15115953;
        acquire.CommandText = "SELECT pg_advisory_lock(@key)";
        acquire.Parameters.AddWithValue("key", (long)lockKey);
        await acquire.ExecuteNonQueryAsync(runnerToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(runnerToken);
        Exception? operationFailure = null;

        try
        {
            // Act
            // WHY: The observer and protected shutdown share this operation; convert its ValueTask once
            // so both can safely inspect/join the same completion without consuming a ValueTask twice.
            var execution = Record.ExceptionAsync(() => ExplainGenericAsync(
                context,
                name,
                "SELECT pg_advisory_lock($1)",
                false,
                lockKey.ToString(CultureInfo.InvariantCulture),
                true,
                cancellation.Token)).AsTask();

            Exception? exception = null;
            Exception? observationFailure = null;

            try
            {
                await WaitForPreparedExecutionLockAsync(
                    blocker,
                    subject.ProcessID,
                    name,
                    execution,
                    runnerToken);
                await cancellation.CancelAsync();
                exception = await execution;
            }
            catch (Exception failure)
            {
                observationFailure = failure;
            }
            finally
            {
                await StopPreparedExecutionAsync(cancellation, execution, observationFailure);
            }

            var after = await PreparedSessionAsync(context, name, runnerToken);
            var sequentialScanAfter = await ReadSequentialScanSettingAsync(context, runnerToken);

            // Assert
            Assert.IsType<OperationCanceledException>(exception, exactMatch: false);
            Assert.Equal("force_custom_plan", after.Mode);
            Assert.Equal(0, after.Count);
            Assert.Equal(originalSequentialScan, sequentialScanAfter);
        }
        catch (Exception failure)
        {
            operationFailure = failure;
        }
        finally
        {
            await CleanupPreparedSessionAsync(context, name, original.Mode, operationFailure);
        }
    }

    /// <summary>Stops and joins the owned command even if cancellation callbacks or its observer fail.</summary>
    private static async Task StopPreparedExecutionAsync(
        CancellationTokenSource cancellation,
        Task<Exception?> execution,
        Exception? observationFailure
    )
    {
        List<Exception>? failures = observationFailure is null ? null : [observationFailure];

        try
        {
            await cancellation.CancelAsync();
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        try
        {
            await execution;
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
            throw new AggregateException("Prepared query observation and shutdown failed.", failures);
        }
    }

    /// <summary>Observes the actual prepared statement blocked in PostgreSQL before requesting cancellation.</summary>
    private static async Task WaitForPreparedExecutionLockAsync(
        NpgsqlConnection observer,
        int processId,
        string statementName,
        Task<Exception?> execution,
        CancellationToken cancellationToken
    )
    {
        await using var command = observer.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_stat_activity "
            + "WHERE pid = @pid AND wait_event_type = 'Lock' AND strpos(query, @name) > 0)";
        command.Parameters.AddWithValue("pid", processId);
        command.Parameters.AddWithValue("name", statementName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (execution.IsCompleted)
            {
                var failure = await execution;

                if (failure is not null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                throw new InvalidOperationException("The prepared query completed before reaching its owned lock.");
            }

            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return;
            }

            await Task.Yield();
        }
    }
}
