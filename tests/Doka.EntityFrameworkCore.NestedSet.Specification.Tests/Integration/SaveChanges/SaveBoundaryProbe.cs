namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes native saves without changing SQL or retaining the context between pooled leases.</summary>
public sealed class SaveBoundaryProbe : DbCommandInterceptor
{
    /// <summary>Gets the number of native payload commands sent through this observer.</summary>
    public int PayloadCommands { get; private set; }

    /// <summary>Gets the number of observed commands targeting hierarchy lock infrastructure.</summary>
    public int LockCommands { get; private set; }

    /// <summary>Gets or sets whether the next payload command should simulate one transient failure.</summary>
    public bool FailNextPayload { get; set; }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Observe(command);

        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        Observe(command);

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
        Observe(command);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);

        return ValueTask.FromResult(result);
    }

    /// <summary>Counts executed boundaries rather than depending on a provider's returning-clause convention.</summary>
    /// <param name="command">The command whose execution is about to begin.</param>
    private void Observe(
        DbCommand command
    )
    {
        if (command.CommandText.Contains("StrictOrderingLocks", StringComparison.Ordinal))
        {
            LockCommands++;
        }

        if (!command.CommandText.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
            && !command.CommandText.Contains("INSERT ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!command.CommandText.Contains("StrictOrderingMarkers", StringComparison.Ordinal)
            && !command.CommandText.Contains("StrictOrderingNodes", StringComparison.Ordinal))
        {
            return;
        }

        PayloadCommands++;

        if (FailNextPayload)
        {
            FailNextPayload = false;

            throw new SaveBoundaryTransientException();
        }
    }
}
