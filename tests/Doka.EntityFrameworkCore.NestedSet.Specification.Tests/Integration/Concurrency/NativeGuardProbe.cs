namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes guard queries and can mutate the tracker at the awaited command boundary.</summary>
internal sealed class NativeGuardProbe : DbCommandInterceptor
{
    private const string QueryTag = "NestedSet native tracked identity guard";

    /// <summary>Gets or sets whether commands belong to the measured mutation.</summary>
    internal bool Armed { get; set; }

    /// <summary>Gets every measured asynchronous read or write command.</summary>
    internal int Commands { get; private set; }

    /// <summary>Gets the bounded native guard membership reads.</summary>
    internal int NativeReads { get; private set; }

    /// <summary>Gets the largest parameter count observed on a native guard query.</summary>
    internal int MaximumNativeParameters { get; private set; }

    /// <summary>Gets or sets a one-shot mutation at the native query's asynchronous command boundary.</summary>
    internal Action? BeforeNativeRead { get; set; }

    /// <summary>Gets or sets a one-shot asynchronous callback before the native guard command executes.</summary>
    internal Func<CancellationToken, Task>? BeforeNativeReadAsync { get; set; }

    /// <summary>Gets the native probes that emitted MySQL's column-collation comparison function.</summary>
    internal int NativeScopeComparisons { get; private set; }

    /// <summary>Gets or sets the exact mapped registry table observed during lock acquisition.</summary>
    internal string? RegistryTable { get; set; }

    /// <summary>Gets or sets a one-shot attachment after the actual registry locking reader executes.</summary>
    internal Action? AfterRegistryRead { get; set; }

    /// <summary>Gets the number of actual registry callbacks executed during the measured mutation.</summary>
    internal int RegistryCallbacks { get; private set; }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        if (Armed
            && RegistryTable is { } table
            && AfterRegistryRead is { } callback
            && command.CommandText.Contains(table, StringComparison.Ordinal))
        {
            RegistryCallbacks++;
            AfterRegistryRead = null;
            callback();
        }

        return ValueTask.FromResult(result);
    }

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
            Commands++;

            if (command.CommandText.Contains(QueryTag, StringComparison.Ordinal))
            {
                NativeReads++;
                MaximumNativeParameters = Math.Max(MaximumNativeParameters, command.Parameters.Count);
                if (command.CommandText.Contains("STRCMP(", StringComparison.Ordinal))
                {
                    NativeScopeComparisons++;
                }

                var callback = BeforeNativeRead;
                BeforeNativeRead = null;
                callback?.Invoke();
                var asynchronousCallback = BeforeNativeReadAsync;
                BeforeNativeReadAsync = null;

                if (asynchronousCallback is not null)
                {
                    return InvokeNativeCallbackAsync(asynchronousCallback, result, cancellationToken);
                }
            }
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>Awaits deterministic cancellation before sending the native command to any provider.</summary>
    private static async ValueTask<InterceptionResult<DbDataReader>> InvokeNativeCallbackAsync(
        Func<CancellationToken, Task> callback,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken
    )
    {
        await callback(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

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
        if (Armed)
        {
            Commands++;
        }

        return ValueTask.FromResult(result);
    }
}
