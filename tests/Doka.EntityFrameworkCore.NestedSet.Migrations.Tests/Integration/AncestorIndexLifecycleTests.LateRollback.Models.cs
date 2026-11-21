using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexLifecycleTests
{
    /// <summary>A comparable snapshot of one complete structural row and its derived shadow marker.</summary>
    /// <param name="Id">The node identity.</param>
    /// <param name="Scope">The isolation scope.</param>
    /// <param name="Tree">The stable tree identity.</param>
    /// <param name="Parent">The direct parent identity.</param>
    /// <param name="Left">The left boundary.</param>
    /// <param name="Right">The right boundary.</param>
    /// <param name="Depth">The structural depth.</param>
    /// <param name="Position">The sibling position.</param>
    /// <param name="Flag">The generated branch marker.</param>
    private sealed record LifecycleState(
        int Id,
        int Scope,
        int Tree,
        int? Parent,
        long Left,
        long Right,
        int Depth,
        long Position,
        bool Flag
    );

    /// <summary>Observes structural writes and fails only at their later generated-value refresh boundary.</summary>
    private sealed class StructuralRefreshProbe : DbCommandInterceptor
    {
        private readonly bool _failRefresh;

        /// <summary>Retains the observation inputs for the fixture operation.</summary>
        /// <param name="failRefresh">Whether to inject the failure after the structural refresh boundary.</param>
        public StructuralRefreshProbe(
            bool failRefresh
        )
        {
            _failRefresh = failRefresh;
        }

        /// <summary>The unique failure text asserted after the transaction has been rolled back.</summary>
        internal const string FailureMessage = "Deliberate failure after ancestor-index structural write.";

        /// <summary>Gets or sets whether the operation under test is being observed.</summary>
        internal bool Armed { get; set; }

        /// <summary>Gets whether a real hierarchy-coordinate UPDATE completed.</summary>
        internal bool StructuralWriteObserved { get; private set; }

        /// <summary>Gets whether the subsequent generated-value refresh was reached.</summary>
        internal bool GeneratedRefreshObserved { get; private set; }

        /// <summary>Gets the database state read within the writer transaction before a possible failure.</summary>
        internal LifecycleState[] DuringMove { get; private set; } = [];

        /// <inheritdoc />
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(MigrationContext.TableName, StringComparison.Ordinal)
                && command.CommandText.Contains("right_bound", StringComparison.Ordinal))
            {
                StructuralWriteObserved = true;
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
            if (!Armed
                || !StructuralWriteObserved
                || GeneratedRefreshObserved
                || !command.CommandText.Contains(AncestorIndexContext.FlagColumn, StringComparison.Ordinal))
            {
                return result;
            }

            GeneratedRefreshObserved = true;
            var context = (AncestorIndexContext)eventData.Context!;
            var sql = context.GetService<ISqlGenerationHelper>();
            string[] columns =
            [
                "entry_id",
                "tree_scope",
                "tree_id",
                "parent_entry",
                "left_bound",
                "right_bound",
                "entry_depth",
                "sibling_position",
                AncestorIndexContext.FlagColumn,
            ];

            // WHY: A raw command shares the exact writer connection/transaction but bypasses EF interception.
            // It proves the generated values really changed before the deliberate failure is raised.
            await using var snapshot = command.Connection!.CreateCommand();
            snapshot.Transaction = command.Transaction;
            snapshot.CommandText = "SELECT "
                + string.Join(", ", columns.Select(sql.DelimitIdentifier))
                + " FROM "
                + sql.DelimitIdentifier(MigrationContext.TableName, context.Schema)
                + " ORDER BY "
                + sql.DelimitIdentifier("entry_id");

            var rows = new List<LifecycleState>();
            await using var reader = await snapshot.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var parentIsNull = await reader.IsDBNullAsync(3, cancellationToken);
                rows.Add(
                    new LifecycleState(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        parentIsNull
                            ? null
                            : reader.GetInt32(3),
                        reader.GetInt64(4),
                        reader.GetInt64(5),
                        reader.GetInt32(6),
                        reader.GetInt64(7),
                        reader.GetBoolean(8)));
            }

            DuringMove = rows.ToArray();

            return _failRefresh
                ? throw new InvalidOperationException(FailureMessage)
                : result;
        }
    }
}
