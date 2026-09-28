namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes commands and injects faults at precise points in a structural mutation.</summary>
public sealed class CommandProbe : DbCommandInterceptor
{
    private bool _failAfterDepthUpdate;
    private bool _failAfterBoundsUpdate;

    /// <summary>Gets the number of UPDATE commands observed since the last reset.</summary>
    public int UpdateCount { get; private set; }

    /// <summary>Gets the number of reader and nonquery commands observed since the last reset.</summary>
    public int CommandCount { get; private set; }

    /// <summary>Gets the number of depth UPDATE statements completed by the database.</summary>
    public int DepthUpdatesCompleted { get; private set; }

    /// <summary>
    /// Gets all affected rows in completed statements assigning depth, including unchanged CASE branches.
    /// </summary>
    public int DepthStatementRowsAffected { get; private set; }

    /// <summary>Gets the number of boundary statements completed by the database.</summary>
    public int BoundsUpdatesCompleted { get; private set; }

    /// <summary>Gets the affected rows from completed boundary statements.</summary>
    public int BoundsRowsAffected { get; private set; }

    /// <summary>Gets or sets cancellation injected after a boundary statement has actually changed rows.</summary>
    public CancellationTokenSource? CancelAfterBoundsUpdate { get; set; }

    /// <summary>Clears counters and configures the fault to inject during the next mutation.</summary>
    /// <param name="failAfterBoundsUpdate">Whether to fail after a boundary UPDATE has reached the database.</param>
    /// <param name="failAfterDepthUpdate">Whether to fail after a depth UPDATE has reached the database.</param>
    public void Reset(
        bool failAfterBoundsUpdate = false,
        bool failAfterDepthUpdate = false
    )
    {
        UpdateCount = 0;
        CommandCount = 0;
        DepthUpdatesCompleted = 0;
        DepthStatementRowsAffected = 0;
        BoundsUpdatesCompleted = 0;
        BoundsRowsAffected = 0;
        CancelAfterBoundsUpdate = null;
        _failAfterBoundsUpdate = failAfterBoundsUpdate;
        _failAfterDepthUpdate = failAfterDepthUpdate;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Inspect(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (command
                .CommandText
                .TrimStart()
                .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            && SqlAssignments.Assigns(command.CommandText, "Depth"))
        {
            // WHY: Throwing after execution proves rollback of an actual depth write, not merely an attempted one.
            DepthUpdatesCompleted++;
            DepthStatementRowsAffected += result;

            if (_failAfterDepthUpdate)
            {
                throw new InjectedCommandException();
            }
        }

        if (result > 0
            && command
                .CommandText
                .TrimStart()
                .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("TreeNode", StringComparison.OrdinalIgnoreCase))
        {
            if (SqlAssignments.Assigns(command.CommandText, "Start")
                || SqlAssignments.Assigns(command.CommandText, "End"))
            {
                BoundsUpdatesCompleted++;
                BoundsRowsAffected += result;

                // WHY: Semantic post-write injection survives command-count reductions and proves real rollback.
                if (CancelAfterBoundsUpdate is { } cancellation)
                {
                    await cancellation.CancelAsync();
                    cancellation.Token.ThrowIfCancellationRequested();
                }

                if (_failAfterBoundsUpdate)
                {
                    throw new InjectedCommandException();
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Inspect(command);

        return ValueTask.FromResult(result);
    }

    /// <summary>Counts command attempts without tying fault injection to an incidental statement number.</summary>
    /// <param name="command">The command about to execute.</param>
    private void Inspect(
        DbCommand command
    )
    {
        CommandCount++;

        if (command
            .CommandText
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            UpdateCount++;
        }
    }
}

/// <summary>Identifies a deliberately injected database-command failure.</summary>
public sealed class InjectedCommandException : Exception;
