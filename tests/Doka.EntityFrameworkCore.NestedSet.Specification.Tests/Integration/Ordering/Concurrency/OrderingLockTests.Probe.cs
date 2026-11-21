namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Records real lock commands and their typed parameters without changing execution.</summary>
internal sealed class OrderingLockProbe : DbCommandInterceptor
{
    /// <summary>Whether this observer has already joined its one-shot first-lock barrier.</summary>
    private bool _joinedBarrier;

    /// <summary>Gets or sets the shared barrier before each context's first server anchor-lock attempt.</summary>
    internal OrderingLockBarrier? FirstAnchorBarrier { get; init; }

    /// <summary>Gets commands issued during the measured operation, in connection execution order.</summary>
    internal List<OrderingLockCommand> Commands { get; } = [];

    /// <summary>Gets attempted server anchor locks, excluding scalar canonicalization and payload statements.</summary>
    internal IEnumerable<OrderingLockCommand> AnchorLocks => Commands.Where(command => IsAnchor(command.Sql));

    /// <summary>Gets commands that attempt to change the domain sort value.</summary>
    internal IEnumerable<OrderingLockCommand> PayloadUpdates =>
        Commands.Where(command =>
        {
            var start = command.Sql.IndexOf("SET ", StringComparison.OrdinalIgnoreCase);
            var end = command.Sql.IndexOf("WHERE ", StringComparison.OrdinalIgnoreCase);

            return start >= 0
                && end > start
                && command
                    .Sql[start..end]
                    .Contains("Name", StringComparison.OrdinalIgnoreCase);
        });

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Record(command);

        if (!_joinedBarrier
            && FirstAnchorBarrier is not null
            && IsAnchor(command.CommandText))
        {
            _joinedBarrier = true;
            await FirstAnchorBarrier.ArriveAsync(cancellationToken);
        }

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
        Record(command);

        return ValueTask.FromResult(result);
    }

    /// <summary>Recognizes both server locking dialects without including infrastructure writes.</summary>
    private static bool IsAnchor(
        string sql
    ) => sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
        || sql.Contains("FOR NO KEY UPDATE", StringComparison.OrdinalIgnoreCase)
        || sql.Contains("WITH (UPDLOCK, HOLDLOCK, ROWLOCK)", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copies parameter values because command disposal and reuse must not alter the observed evidence.
    /// </summary>
    private void Record(
        DbCommand command
    )
    {
        var parameters = command
            .Parameters
            .Cast<DbParameter>()
            .Select(parameter => new OrderingLockParameter(
                parameter.Value is byte[] binary
                    ? binary.ToArray()
                    : parameter.Value,
                parameter.DbType))
            .ToArray();

        Commands.Add(new OrderingLockCommand(command.CommandText, parameters));
    }
}
