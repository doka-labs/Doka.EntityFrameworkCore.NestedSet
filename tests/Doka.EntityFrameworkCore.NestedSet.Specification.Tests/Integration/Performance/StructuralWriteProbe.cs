namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Records actual affected-row counts, including unchanged values that still incurred a database write.
/// </summary>
internal sealed class StructuralWriteProbe : DbCommandInterceptor
{
    private readonly string _table;
    private readonly string _left;
    private readonly string _right;

    /// <summary>Observes the selected mapped table and its exact boundary assignment names.</summary>
    internal StructuralWriteProbe(
        string table = "TreeNode",
        string left = "Start",
        string right = "End"
    )
    {
        _table = table;
        _left = left;
        _right = right;
    }

    /// <summary>Gets or sets cancellation injected after the first real interval update completes.</summary>
    internal CancellationTokenSource? CancelAfterBounds { get; init; }

    /// <summary>Gets completed structural updates with their affected-row counts.</summary>
    internal List<(string Sql, int Rows)> NodeUpdates { get; } = [];

    /// <summary>Gets commands issued while the operation owns its transaction and lock.</summary>
    internal List<string> Commands { get; } = [];

    /// <summary>Gets structural updates assigning one or both interval columns.</summary>
    internal List<(string Sql, int Rows)> BoundsWrites =>
        NodeUpdates
            .Where(write => SqlAssignments.Assigns(write.Sql, _left) || SqlAssignments.Assigns(write.Sql, _right))
            .ToList();

    /// <summary>Reports measurements in a failing assertion so the unoptimized baseline remains reproducible.</summary>
    internal string Describe() => $"Commands={Commands.Count}; updates={NodeUpdates.Count}; "
        + $"affected rows={NodeUpdates.Sum(write => write.Rows)}; boundary updates={BoundsWrites.Count}; "
        + $"boundary rows={BoundsWrites.Sum(write => write.Rows)}";

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Commands.Add(command.CommandText);

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
        Commands.Add(command.CommandText);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (command
                .CommandText
                .TrimStart()
                .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains(_table, StringComparison.OrdinalIgnoreCase))
        {
            NodeUpdates.Add((command.CommandText, result));

            if (BoundsWrites.Count > 0
                && CancelAfterBounds is { } cancellation)
            {
                // WHY: Abort after SQL changed rows so the rollback must undo the combined assignment.
                await cancellation.CancelAsync();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        }

        return result;
    }
}
