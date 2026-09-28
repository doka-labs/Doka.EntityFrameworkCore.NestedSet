namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes structural SQL and injects deterministic faults at repair batch boundaries.</summary>
public sealed class EnterpriseProbe : DbCommandInterceptor
{
    private readonly string _tableName;
    private readonly bool _captureParameterBindings;

    /// <summary>Creates an observer for one hierarchy table without counting lock-table updates.</summary>
    /// <param name="tableName">The mapped hierarchy table name used by the test fixture.</param>
    /// <param name="captureParameterBindings">Whether plan probes retain complete provider-native parameter metadata.</param>
    public EnterpriseProbe(
        string tableName = "TreeNode",
        bool captureParameterBindings = false
    )
    {
        _tableName = tableName;
        _captureParameterBindings = captureParameterBindings;
    }

    /// <summary>Gets the commands executed by the observed context.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Gets parameter values paired with each observed command.</summary>
    public List<object?[]> ParameterValues { get; } = [];

    /// <summary>Gets parameter names needed to explain an observed command with its original bindings.</summary>
    public List<string[]> ParameterNames { get; } = [];

    /// <summary>Gets optional cloned bindings that preserve native parameter types for execution-plan replay.</summary>
    public List<DbParameter[]> ParameterBindings { get; } = [];

    /// <summary>Gets the number of attempted hierarchy UPDATE commands.</summary>
    public int NodeUpdates { get; private set; }

    /// <summary>Gets the number of hierarchy UPDATE commands that reached the database.</summary>
    public int CompletedNodeUpdates { get; private set; }

    /// <summary>Gets the largest parameter count of a hierarchy UPDATE command.</summary>
    public int MaximumUpdateParameters { get; private set; }

    /// <summary>Gets the number of fully materialized hierarchy entities.</summary>
    public int MaterializedNodes { get; private set; }

    /// <summary>Gets or sets whether the second hierarchy UPDATE fails before execution.</summary>
    public bool FailSecondNodeUpdate { get; set; }

    /// <summary>Gets or sets a source canceled immediately before the second hierarchy UPDATE.</summary>
    public CancellationTokenSource? CancelSecondNodeUpdate { get; set; }

    /// <summary>Counts hierarchy materialization reported by the shared singleton observer.</summary>
    /// <param name="entity">The entity initialized by EF for this observer's context.</param>
    internal void RecordMaterialization(
        object entity
    )
    {
        if (entity is TreeNode or ConcurrentNode or BinaryNode or TextNode)
        {
            MaterializedNodes++;
        }
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        await InspectAsync(command);

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
        if (IsNodeUpdate(command))
        {
            CompletedNodeUpdates++;
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
        await InspectAsync(command);

        return result;
    }

    /// <summary>Records command shape and fails only after an earlier repair batch has executed.</summary>
    /// <param name="command">The command about to execute.</param>
    /// <returns>A task that completes after inspection and any cancellation callbacks.</returns>
    private async ValueTask InspectAsync(
        DbCommand command
    )
    {
        Commands.Add(command.CommandText);
        ParameterValues.Add(
            command
                .Parameters
                .Cast<DbParameter>()
                .Select(parameter => parameter.Value)
                .ToArray());

        ParameterNames.Add(
            command
                .Parameters
                .Cast<DbParameter>()
                .Select(parameter => parameter.ParameterName)
                .ToArray());

        if (_captureParameterBindings)
        {
            // WHY: A value alone loses native metadata, such as smallint[] versus bytea for a PostgreSQL byte array.
            ParameterBindings.Add(
                command
                    .Parameters
                    .Cast<DbParameter>()
                    .Select(CloneParameter)
                    .ToArray());
        }

        if (!IsNodeUpdate(command))
        {
            return;
        }

        NodeUpdates++;
        MaximumUpdateParameters = Math.Max(MaximumUpdateParameters, command.Parameters.Count);

        if (NodeUpdates == 2)
        {
            // WHY: Failing before batch two proves rollback of batch one, rather than an untouched transaction.
            if (CancelSecondNodeUpdate is { } cancellation)
            {
                // WHY: Complete cancellation before throwing at the boundary preceding the second repair batch.
                await cancellation.CancelAsync();
                cancellation.Token.ThrowIfCancellationRequested();
            }

            if (FailSecondNodeUpdate)
            {
                throw new InjectedCommandException();
            }
        }
    }

    /// <summary>Snapshots provider metadata before EF can reuse or dispose the original command.</summary>
    private static DbParameter CloneParameter(
        DbParameter parameter
    )
    {
        if (parameter is ICloneable cloneable)
        {
            return (DbParameter)cloneable.Clone();
        }

        if (parameter is Microsoft.Data.Sqlite.SqliteParameter sqlite)
        {
            return new Microsoft.Data.Sqlite.SqliteParameter(sqlite.ParameterName, sqlite.SqliteType)
            {
                Value = sqlite.Value,
                Size = sqlite.Size,
                IsNullable = sqlite.IsNullable,
                SourceColumn = sqlite.SourceColumn,
                SourceColumnNullMapping = sqlite.SourceColumnNullMapping,
            };
        }

        throw new InvalidOperationException($"Plan replay cannot clone {parameter.GetType().Name} parameter metadata.");
    }

    /// <summary>Excludes infrastructure locking writes from repair command counts.</summary>
    private bool IsNodeUpdate(
        DbCommand command
    ) => command
            .CommandText
            .TrimStart()
            .StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
        && command.CommandText.Contains(_tableName, StringComparison.OrdinalIgnoreCase);
}
