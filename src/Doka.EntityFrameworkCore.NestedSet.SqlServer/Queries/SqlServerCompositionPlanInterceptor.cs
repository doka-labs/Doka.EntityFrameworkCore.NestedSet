namespace Doka.EntityFrameworkCore.NestedSet.SqlServer;

/// <summary>Plans tagged hierarchy compositions for every selection size instead of the first sniffed values.</summary>
internal sealed class SqlServerCompositionPlanInterceptor : DbCommandInterceptor
{
    // WHY: SQL Server reuses the plan compiled for the first parameter values. A plan built for one
    // selected source repeated subtree and path work for each later source; even fresh compiles chose
    // that per-row shape for small exact estimates. Average selectivity kept every measured composition
    // profile within its population-pass budget; the hint does not fix a physical plan.
    // WHY: Parallel nested loops rebuilt an uncorrelated inner relation once per worker thread, so
    // completed path failures repeated with the core count. MAXDOP 1 removes that per-worker repetition.
    private const string Hint = "OPTION (OPTIMIZE FOR UNKNOWN, MAXDOP 1)";

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Apply(command);

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
        Apply(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        Apply(command);

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Apply(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        Apply(command);

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Apply(command);

        return ValueTask.FromResult(result);
    }

    /// <summary>Appends the statement-level hint once to one tagged single-statement command.</summary>
    /// <param name="command">The command about to execute.</param>
    private static void Apply(
        DbCommand command
    )
    {
        var text = command.CommandText;

        if (!NestedSetCompositionTag.Marks(text))
        {
            return;
        }

        // WHY: EF emits one query, ExecuteUpdate or ExecuteDelete statement per tagged command.
        // OPTION must close that statement, so an optional terminator stays after the hint.
        var statement = text.AsSpan().TrimEnd();
        var terminated = statement.EndsWith(";", StringComparison.Ordinal);

        if (terminated)
        {
            statement = statement[..^1].TrimEnd();
        }

        if (statement.EndsWith(Hint, StringComparison.Ordinal))
        {
            return;
        }

        command.CommandText = string.Concat(statement, Environment.NewLine, Hint, terminated ? ";" : "");
    }
}
