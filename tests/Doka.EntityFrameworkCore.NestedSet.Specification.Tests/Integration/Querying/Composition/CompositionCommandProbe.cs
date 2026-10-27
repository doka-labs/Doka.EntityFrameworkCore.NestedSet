namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Counts asynchronous commands and rejects synchronous I/O in composition contracts.</summary>
internal sealed class CompositionCommandProbe : DbCommandInterceptor
{
    /// <summary>Gets the number of asynchronous commands observed since the last reset.</summary>
    internal int CommandCount { get; private set; }

    /// <summary>Gets the number of hierarchy entity instances materialized by the observed context.</summary>
    internal int MaterializedNodes { get; private set; }

    /// <summary>Excludes fixture initialization from the query's command budget.</summary>
    internal void Reset()
    {
        CommandCount = 0;
        MaterializedNodes = 0;
    }

    /// <summary>Counts this feature's entity type rather than relying on another suite's type allowlist.</summary>
    internal void RecordMaterialization(
        object entity
    )
    {
        if (entity is CompositionNode)
        {
            MaterializedNodes++;
        }
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    ) => throw new InvalidOperationException("Hierarchy composition must not execute synchronous database commands.");

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    ) => throw new InvalidOperationException("Hierarchy composition must not execute synchronous database commands.");

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    ) => throw new InvalidOperationException("Hierarchy composition must not execute synchronous database commands.");

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        CommandCount++;

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
        CommandCount++;

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        CommandCount++;

        return ValueTask.FromResult(result);
    }
}
