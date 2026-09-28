namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Measures final scalar reads after imports detach, excluding ordering and placement queries.</summary>
public sealed class BulkIntervalRefreshProbe : DbCommandInterceptor
{
    private readonly string _table;
    private readonly HashSet<Guid> _readers = [];

    /// <summary>Selects the mapped table whose final import refresh is observed.</summary>
    public BulkIntervalRefreshProbe(
        string table
    )
    {
        _table = table;
    }

    /// <summary>Gets or sets whether the insertion save has completed and observations are active.</summary>
    public bool Inserted { get; set; }

    /// <summary>Gets final refresh SQL without collecting payload values or credentials.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Gets each refresh command's parameter count for the old/new query-shape measurement.</summary>
    public List<int> ParameterCounts { get; } = [];

    /// <summary>Gets the number of reader operations reported by EF when observed readers are disposed.</summary>
    public List<int> ReadCalls { get; } = [];

    /// <summary>Gets or sets a one-shot database mutation or cancellation immediately before final refresh.</summary>
    public Func<DbCommand, CancellationToken, Task>? BeforeRefresh { get; set; }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        var from = command.CommandText.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);

        if (!Inserted
            || from < 0
            || !command.CommandText.Contains(_table, StringComparison.OrdinalIgnoreCase)
            || !command.CommandText.Contains(NestedSetDiagnostics.BulkRefreshTag, StringComparison.Ordinal))
        {
            return result;
        }

        Commands.Add(command.CommandText);
        ParameterCounts.Add(command.Parameters.Count);
        _readers.Add(eventData.CommandId);

        if (BeforeRefresh is { } action)
        {
            BeforeRefresh = null;
            await action(command, cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult DataReaderDisposing(
        DbCommand command,
        DataReaderDisposingEventData eventData,
        InterceptionResult result
    )
    {
        // WHY: EF exposes this observation synchronously; no database I/O or asynchronous cleanup is performed here.
        if (_readers.Remove(eventData.CommandId))
        {
            ReadCalls.Add(eventData.ReadCount);
        }

        return result;
    }
}
