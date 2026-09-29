namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Counts completed commands in an untimed diagnostic pass without retaining SQL or parameter payloads.
/// </summary>
internal sealed class CommandObservation : DbCommandInterceptor
{
    /// <summary>Gets the executed EF command count, excluding transaction API calls.</summary>
    internal int Commands { get; private set; }

    /// <summary>Gets the completed non-query hierarchy UPDATE command count.</summary>
    internal int Updates { get; private set; }

    /// <summary>
    /// Gets provider-reported affected rows from non-query hierarchy updates, including repeated updates.
    /// </summary>
    internal long UpdatedRows { get; private set; }

    /// <summary>Starts the operation-only observation after the entire scenario has been prepared.</summary>
    internal void Reset()
    {
        Commands = 0;
        Updates = 0;
        UpdatedRows = 0;
    }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        Commands++;

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        Commands++;

        if (command
                .CommandText
                .TrimStart()
                .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("BenchmarkNodes", StringComparison.Ordinal))
        {
            Updates++;
            UpdatedRows += result;
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default
    )
    {
        Commands++;

        return ValueTask.FromResult(result);
    }
}
