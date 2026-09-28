namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTrackerTests
{
    /// <summary>Fails only after ordinary payload writes, while structural repair is about to begin.</summary>
    private sealed class TrackerFailureProbe : DbCommandInterceptor
    {
        /// <summary>Gets or sets whether the next late structural command is rejected.</summary>
        internal bool FailAfterPayload { get; set; } = true;

        /// <summary>Gets the number of completed payload updates.</summary>
        internal int CompletedPayloadUpdates { get; private set; }

        /// <summary>Gets the number of completed unrelated identity inserts.</summary>
        internal int CompletedAdditionInserts { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Inspect(command);

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
            Inspect(command);

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
            RecordCompletion(command);

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
            RecordCompletion(command);

            return ValueTask.FromResult(result);
        }

        /// <summary>
        ///     Rejects repair after a completed payload UPDATE, leaving the coordinator to restore state.
        /// </summary>
        private void Inspect(
            DbCommand command
        )
        {
            var setters = GetSetters(command);
            if (FailAfterPayload
                && CompletedPayloadUpdates > 0
                && (setters.Contains("\"Left\"", StringComparison.Ordinal)
                    || setters.Contains("\"Right\"", StringComparison.Ordinal)
                    || setters.Contains("\"Position\"", StringComparison.Ordinal)))
            {
                // WHY: An earlier database UPDATE must succeed to distinguish rollback from early rejection.
                throw new TrackerInjectedException();
            }
        }

        /// <summary>Records database completion without counting failed command attempts.</summary>
        private void RecordCompletion(
            DbCommand command
        )
        {
            if (GetSetters(command)
                .Contains("\"Name\"", StringComparison.Ordinal))
            {
                CompletedPayloadUpdates++;
            }

            if (NormalizedSql(command)
                .Contains("INSERT INTO \"TrackerAdditions\"", StringComparison.Ordinal))
            {
                CompletedAdditionInserts++;
            }
        }

        /// <summary>
        ///     Normalizes provider identifier delimiters without making assumptions about command batching.
        /// </summary>
        /// <param name="command">The observed generated command containing no literal application payload.</param>
        private static string NormalizedSql(
            DbCommand command
        ) => command
            .CommandText
            .Replace('`', '"')
            .Replace('[', '"')
            .Replace(']', '"');

        /// <summary>Limits inspection to columns assigned by an UPDATE of this test's ordered table.</summary>
        private static string GetSetters(
            DbCommand command
        )
        {
            var sql = NormalizedSql(command);
            if (!sql.Contains("UPDATE ", StringComparison.Ordinal)
                || !sql.Contains("\"TrackerNodes\"", StringComparison.Ordinal))
            {
                return "";
            }

            var start = sql.IndexOf("SET ", StringComparison.Ordinal);
            var end = sql.IndexOf("WHERE ", Math.Max(0, start), StringComparison.Ordinal);
            if (start < 0)
            {
                return "";
            }

            return end < 0 ? sql[start..] : sql[start..end];
        }
    }

    /// <summary>Identifies the deliberately injected late repair failure without masking a provider error.</summary>
    private sealed class TrackerInjectedException : Exception;
}
