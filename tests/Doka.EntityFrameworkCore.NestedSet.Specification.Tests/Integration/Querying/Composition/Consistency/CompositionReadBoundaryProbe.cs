namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Coordinates cancellation or a writer after a genuine asynchronous provider row read.</summary>
internal sealed class CompositionReadBoundaryProbe : DbCommandInterceptor
{
    private readonly Func<CancellationToken, Task> _afterFirstRead;

    /// <summary>Creates a per-context observer without replacing the provider's data or execution.</summary>
    internal CompositionReadBoundaryProbe(
        Func<CancellationToken, Task> afterFirstRead
    )
    {
        _afterFirstRead = afterFirstRead;
    }

    /// <summary>Gets or sets whether only the measured composition query should be observed.</summary>
    internal bool Armed { get; set; }

    /// <summary>Gets the number of completed first-row reads in the measured query.</summary>
    internal int FirstRows { get; private set; }

    /// <summary>Gets the actual token passed to the asynchronous command interceptor.</summary>
    internal CancellationToken CommandToken { get; private set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        if (Armed)
        {
            CommandToken = cancellationToken;
        }

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
        if (!Armed)
        {
            return ValueTask.FromResult(result);
        }

        // WHY: The existing forwarding reader exposes the first successful ReadAsync without duplicating
        // a provider adapter. This is a real statement boundary, never an invented barrier between LINQ legs.
        return ValueTask.FromResult<DbDataReader>(
            new ConcurrentCapacityLockReader(result, () => FirstRows++, _afterFirstRead));
    }
}
