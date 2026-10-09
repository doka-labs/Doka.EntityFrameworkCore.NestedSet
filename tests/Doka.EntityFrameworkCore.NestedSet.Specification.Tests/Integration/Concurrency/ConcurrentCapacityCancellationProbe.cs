namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes an actual server lock attempt without replacing or delaying provider execution.</summary>
internal sealed class ConcurrentCapacityCancellationProbe : DbCommandInterceptor
{
    /// <summary>Gets or sets whether the contender's hierarchy operation is being observed.</summary>
    internal bool Armed { get; set; }

    /// <summary>Gets the signal raised immediately before dispatching the registry row-lock query.</summary>
    internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the number of exact registry row-lock queries dispatched.</summary>
    internal int LockAttempts { get; private set; }

    /// <summary>Gets the number of registry queries whose first locked row was read successfully.</summary>
    internal int AcquiredLocks { get; private set; }

    /// <summary>Gets the number of attempted structural writes following the blocked acquisition.</summary>
    internal int StructuralWrites { get; private set; }

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
            ObserveStructuralWrite(command);

            if (IsRegistryLock(eventData.Context, command))
            {
                LockAttempts++;
                Attempted.TrySetResult();
            }
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
        if (Armed && IsRegistryLock(eventData.Context, command))
        {
            // WHY: SQL Server can return reader metadata while the first row still waits for its lock.
            // Acquisition is observable only after reading that row successfully.
            return ValueTask.FromResult<DbDataReader>(new ConcurrentCapacityLockReader(result, () => AcquiredLocks++));
        }

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
        if (Armed)
        {
            ObserveStructuralWrite(command);
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>Matches the provider-specific locking read of an existing tree registry.</summary>
    private static bool IsRegistryLock(
        DbContext? context,
        DbCommand command
    ) => NestedSetTestInfrastructure.ReferencesRegistry(context, command.CommandText)
        && (command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
            || command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.OrdinalIgnoreCase)
            || command.CommandText.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase));

    /// <summary>Counts hierarchy writes without retaining SQL or parameter arrays.</summary>
    private void ObserveStructuralWrite(
        DbCommand command
    )
    {
        var statement = command.CommandText.TrimStart();

        if (statement.Contains(nameof(ConcurrentNode), StringComparison.OrdinalIgnoreCase)
            && (statement.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)))
        {
            StructuralWrites++;
        }
    }
}
