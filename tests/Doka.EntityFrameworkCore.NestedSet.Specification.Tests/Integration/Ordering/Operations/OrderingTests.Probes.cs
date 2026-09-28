namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Observes the payload/structure boundary and injects a failure only after the payload reaches the database.
/// </summary>
public sealed class OrderingSaveProbe : DbCommandInterceptor
{
    /// <summary>Gets or sets whether structural repair fails after a completed payload save.</summary>
    public bool FailAfterPayload { get; set; }

    /// <summary>Gets or sets a cancellation source canceled at the same late repair boundary.</summary>
    public CancellationTokenSource? CancelAfterPayload { get; set; }

    /// <summary>Gets the number of payload UPDATE commands that completed at the database.</summary>
    public int CompletedPayloadUpdates { get; private set; }

    /// <summary>Gets the number of attempted structural UPDATE commands.</summary>
    public int StructuralUpdates { get; private set; }

    /// <summary>Gets the number of domain node instances materialized by this observed context.</summary>
    public int MaterializedNodes { get; private set; }

    /// <summary>Gets commands executed by the observed context.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Starts the measured interval after the caller has loaded the entity it intends to modify.</summary>
    public void ResetMaterializedNodes() => MaterializedNodes = 0;

    /// <summary>Records a domain instance without retaining it or the observed context.</summary>
    internal void RecordMaterialization(
        object entity
    )
    {
        if (entity is OrderingNode)
        {
            MaterializedNodes++;
        }
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        await InspectAsync(command);

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        await InspectAsync(command);

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        RecordCompletion(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        RecordCompletion(command);

        return ValueTask.FromResult(result);
    }

    /// <summary>Fails or cancels before repair only when an earlier domain update has really executed.</summary>
    private async ValueTask InspectAsync(
        DbCommand command
    )
    {
        Commands.Add(command.CommandText);
        var setters = GetSetters(command);
        if (!IsStructural(setters))
        {
            return;
        }

        StructuralUpdates++;
        if (CompletedPayloadUpdates == 0)
        {
            return;
        }

        if (CancelAfterPayload is { } cancellation)
        {
            await cancellation.CancelAsync();
            cancellation.Token.ThrowIfCancellationRequested();
        }

        if (FailAfterPayload)
        {
            // WHY: The observer deliberately leaves a successful payload UPDATE for rollback to undo.
            throw new OrderingInjectedException();
        }
    }

    /// <summary>
    ///     Records database completion across providers using either reader or nonquery UPDATE execution.
    /// </summary>
    private void RecordCompletion(
        DbCommand command
    )
    {
        var setters = GetSetters(command);
        if (setters.Contains("Name", StringComparison.OrdinalIgnoreCase)
            || setters.Contains("Priority", StringComparison.OrdinalIgnoreCase)
            || setters.Contains("Payload", StringComparison.OrdinalIgnoreCase))
        {
            CompletedPayloadUpdates++;
        }
    }

    /// <summary>Extracts only assigned columns, excluding predicates and provider RETURNING clauses.</summary>
    private static string GetSetters(
        DbCommand command
    )
    {
        var sql = command.CommandText;
        var update = sql.IndexOf("UPDATE ", StringComparison.OrdinalIgnoreCase);
        if (update < 0 || !sql.Contains("OrderingNodes", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var start = sql.IndexOf("SET ", update, StringComparison.OrdinalIgnoreCase);
        var end = sql.IndexOf("WHERE ", Math.Max(0, start), StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "";
        }

        return end < 0 ? sql[start..] : sql[start..end];
    }

    /// <summary>Recognizes hierarchy writes without counting lock-table or ordinary domain updates.</summary>
    private static bool IsStructural(
        string setters
    ) => setters.Contains("Left", StringComparison.OrdinalIgnoreCase)
        || setters.Contains("Right", StringComparison.OrdinalIgnoreCase)
        || setters.Contains("Depth", StringComparison.OrdinalIgnoreCase)
        || setters.Contains("Position", StringComparison.OrdinalIgnoreCase)
        || setters.Contains("ParentId", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Identifies the deterministic late failure injected by ordering transaction tests.</summary>
public sealed class OrderingInjectedException : Exception;
