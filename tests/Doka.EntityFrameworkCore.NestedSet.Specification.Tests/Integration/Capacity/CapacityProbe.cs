namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes capacity operations with bounded command counters and injected fault boundaries.</summary>
internal sealed class CapacityProbe : DbCommandInterceptor
{
    /// <summary>Gets the number of attempted bounded repair commands.</summary>
    internal int RepairCommands { get; private set; }

    /// <summary>Gets the number of repair commands completed before a fault boundary.</summary>
    internal int CompletedRepairs { get; private set; }

    /// <summary>Gets the total rows reported by completed repair commands.</summary>
    internal long RepairedRows { get; private set; }

    /// <summary>Gets the largest repair parameter count without retaining any bindings.</summary>
    internal int MaximumRepairParameters { get; private set; }

    /// <summary>Gets the largest observed command parameter count.</summary>
    internal int MaximumCommandParameters { get; private set; }

    /// <summary>Gets the largest observed repair SQL length.</summary>
    internal int MaximumRepairSqlLength { get; private set; }

    /// <summary>Gets the number of completed public import payload saves.</summary>
    internal int CompletedSaves { get; private set; }

    /// <summary>Gets whether the late fault was injected after earlier batches completed.</summary>
    internal bool ReachedFailure { get; private set; }

    /// <summary>Gets or sets the repair command to fail before execution, or zero to disable injection.</summary>
    internal int FailRepairCommand { get; set; }

    /// <summary>Gets or sets whether to fail the final bulk result refresh.</summary>
    internal bool FailBulkRefresh { get; set; }

    /// <summary>Gets or sets a source to cancel at the selected late failure boundary.</summary>
    internal CancellationTokenSource? Cancellation { get; set; }

    /// <summary>Counts successful EF payload waves without retaining caller-owned entities.</summary>
    internal void Saved() => CompletedSaves++;

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);

        if (IsRepair(command))
        {
            RepairCommands++;
            MaximumRepairParameters = Math.Max(MaximumRepairParameters, command.Parameters.Count);
            MaximumRepairSqlLength = Math.Max(MaximumRepairSqlLength, command.CommandText.Length);

            if (RepairCommands == FailRepairCommand)
            {
                await FailAsync(cancellationToken: cancellationToken);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (IsRepair(command))
        {
            CompletedRepairs++;
            RepairedRows += result;
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);

        if (FailBulkRefresh
            && command.CommandText.Contains(NestedSetDiagnostics.BulkRefreshTag, StringComparison.Ordinal))
        {
            await FailAsync(cancellationToken: cancellationToken);
        }

        return result;
    }

    /// <summary>Records command cardinality without retaining command or parameter objects.</summary>
    private void Observe(
        DbCommand command
    )
    {
        MaximumCommandParameters = Math.Max(MaximumCommandParameters, command.Parameters.Count);
    }

    /// <summary>Injects a fault or fully completed cancellation at the same late command boundary.</summary>
    /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
    private async Task FailAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ReachedFailure = true;

        if (Cancellation is { } source)
        {
            await source.CancelAsync();
            source.Token.ThrowIfCancellationRequested();
        }

        throw new InjectedCommandException();
    }

    /// <summary>Identifies mapped CASE repairs without counting registry updates or setup writes.</summary>
    private static bool IsRepair(
        DbCommand command
    ) => command
            .CommandText
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
        && command.CommandText.Contains("TreeNode", StringComparison.OrdinalIgnoreCase)
        && command.CommandText.Contains("CASE", StringComparison.OrdinalIgnoreCase);
}
