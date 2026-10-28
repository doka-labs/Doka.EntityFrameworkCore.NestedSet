using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class QueryPlanReplayTests
{
    /// <summary>Returns framework-owned tabular results without a second fake reader implementation.</summary>
    private static DataTableReader Results(
        params DataTable[] tables
    ) => new DataTableReader(tables);

    /// <summary>Creates the one-column shape returned for JSON, TREE, and XML plans.</summary>
    private static DataTable Plan(
        string value,
        string column = "EXPLAIN"
    )
    {
        var table = new DataTable();
        table.Columns.Add(column, typeof(string));
        table.Rows.Add(value);

        return table;
    }

    /// <summary>Uses the exact three-column MySQL warning shape, including its integer error code.</summary>
    private static DataTable Warnings(
        int code,
        string message
    )
    {
        var table = new DataTable();
        table.Columns.Add("Level", typeof(string));
        table.Columns.Add("Code", typeof(int));
        table.Columns.Add("Message", typeof(string));
        table.Rows.Add("Warning", code, message);

        return table;
    }

    /// <summary>Exposes a nonstring SELECT result so XML replay must drain it without reading it as a plan.</summary>
    private static DataTable ScalarRows()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(42);

        return table;
    }

    /// <summary>Keeps a real local connection while substituting only its diagnostic command and result sets.</summary>
    private sealed class PlanReplayConnection(Func<DbDataReader> reader) : SqliteConnection("Data Source=:memory:")
    {
        /// <summary>Gets the raw diagnostic command after the helper applies its own deadline and statement.</summary>
        internal PlanReplayCommand? Command { get; private set; }

        /// <inheritdoc />
        protected override DbCommand CreateDbCommand()
        {
            Command = new PlanReplayCommand(reader);

            return Command;
        }
    }

    /// <summary>Records the raw deadline and returns controlled framework reader results for the actual helper.</summary>
    private sealed class PlanReplayCommand(Func<DbDataReader> reader) : DbCommand
    {
        private readonly SqliteCommand _parameters = new();

        /// <inheritdoc />
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        /// <inheritdoc />
        public override int CommandTimeout { get; set; } = 30;

        /// <inheritdoc />
        public override CommandType CommandType { get; set; }

        /// <inheritdoc />
        public override bool DesignTimeVisible { get; set; }

        /// <inheritdoc />
        public override UpdateRowSource UpdatedRowSource { get; set; }

        /// <inheritdoc />
        protected override DbConnection? DbConnection { get; set; }

        /// <inheritdoc />
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;

        /// <inheritdoc />
        protected override DbTransaction? DbTransaction { get; set; }

        /// <inheritdoc />
        public override void Cancel() => throw new NotSupportedException();

        /// <inheritdoc />
        public override int ExecuteNonQuery() => throw new NotSupportedException();

        /// <inheritdoc />
        public override object ExecuteScalar() => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Prepare() => throw new NotSupportedException();

        /// <inheritdoc />
        protected override DbParameter CreateDbParameter() => _parameters.CreateParameter();

        /// <inheritdoc />
        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        ) => reader();

        /// <inheritdoc />
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken
        ) => Task.FromResult(reader());

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            await _parameters.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
