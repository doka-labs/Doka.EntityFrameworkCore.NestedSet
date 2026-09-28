namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Fails at final result refresh after all writes, independently of the batch count.</summary>
public sealed class BulkRefreshFailure : DbCommandInterceptor
{
    private readonly string _table;
    private readonly CancellationTokenSource? _cancellation;

    /// <summary>Selects the imported table and optionally cancels instead of throwing an injected failure.</summary>
    public BulkRefreshFailure(
        string table,
        CancellationTokenSource? cancellation = null
    )
    {
        _table = table;
        _cancellation = cancellation;
    }

    /// <summary>Gets or sets whether the import's SaveChanges callback has completed.</summary>
    public bool Inserted { get; set; }

    /// <summary>Gets whether final refresh was reached after the insertion wave.</summary>
    public bool ReachedRefresh { get; private set; }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        var sql = command.CommandText;

        // WHY: Reordering and refresh can project the same structural columns. The fixed payload-free tag marks
        // the exact final read without relying on provider SQL syntax or current query-plan shape.
        if (!Inserted
            || ReachedRefresh
            || !sql.Contains(_table, StringComparison.OrdinalIgnoreCase)
            || !sql.Contains(NestedSetDiagnostics.BulkRefreshTag, StringComparison.Ordinal))
        {
            return result;
        }

        ReachedRefresh = true;

        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync();
            _cancellation.Token.ThrowIfCancellationRequested();
        }

        throw new InjectedCommandException();
    }
}
