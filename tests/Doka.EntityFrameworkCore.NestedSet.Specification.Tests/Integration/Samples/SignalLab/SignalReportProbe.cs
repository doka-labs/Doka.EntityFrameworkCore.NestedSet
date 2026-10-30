namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes the actual sample's command, parameter and materialization boundaries.</summary>
internal sealed class SignalReportProbe : DbCommandInterceptor
{
    /// <summary>Gets the asynchronous reader statements after fixture initialization.</summary>
    internal List<string> Readers { get; } = [];

    /// <summary>Gets scalar parameter values without retaining provider command instances.</summary>
    internal List<object?> ParameterValues { get; } = [];

    /// <summary>Gets the number of materialized domain entities.</summary>
    internal int Entities { get; private set; }

    /// <summary>Optionally stages one concurrent commit after the catalog statement established its snapshot.</summary>
    internal Func<CancellationToken, Task>? AfterFirstReader { get; set; }

    /// <summary>Excludes setup from the report's observable budgets.</summary>
    internal void Reset()
    {
        Readers.Clear();
        ParameterValues.Clear();
        Entities = 0;
    }

    /// <summary>Counts actual entity instances; scalar aggregate DTOs never enter this callback.</summary>
    internal void Materialized(object entity) => Entities++;

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result
    ) => throw new InvalidOperationException("The signal sample must execute reader I/O asynchronously.");

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result
    ) => throw new InvalidOperationException("The signal sample must execute command I/O asynchronously.");

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result
    ) => throw new InvalidOperationException("The signal sample must execute scalar I/O asynchronously.");

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Readers.Add(command.CommandText);
        ParameterValues.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value));

        return ValueTask.FromResult(result);
    }
    /// <inheritdoc />
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        if (Readers.Count == 1 && AfterFirstReader is { } callback)
        {
            AfterFirstReader = null;
            await callback(cancellationToken);
        }

        return result;
    }

}
