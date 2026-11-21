namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Fails the actual later ordering UPDATE once, after its earlier payload SQL succeeded.</summary>
    private sealed class PayloadOwnerHierarchyFailure : DbCommandInterceptor
    {
        private bool _armed;
        private bool _failed;

        /// <summary>Gets the exact fault distinguishing structural rollback from early rejection.</summary>
        internal Exception Error { get; } = new InvalidOperationException("Injected payload-owner hierarchy failure.");

        /// <summary>Marks the native payload save as completed without failing its completion callbacks.</summary>
        internal void Arm() => _armed = true;

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

        /// <summary>Inspects UPDATE setters using the existing tracker-fault delimiter normalization.</summary>
        private void Inspect(
            DbCommand command
        )
        {
            if (!_armed || _failed)
            {
                return;
            }

            var sql = command
                .CommandText
                .Replace('`', '"')
                .Replace('[', '"')
                .Replace(']', '"');

            var start = sql.IndexOf("SET ", StringComparison.Ordinal);
            var end = sql.IndexOf("WHERE ", Math.Max(0, start), StringComparison.Ordinal);

            if (!sql.Contains("UPDATE ", StringComparison.Ordinal)
                || !sql.Contains("\"NavigationSaveNodes\"", StringComparison.Ordinal)
                || start < 0)
            {
                return;
            }

            var setters = end < 0
                ? sql[start..]
                : sql[start..end];

            if (setters.Contains("\"Left\"", StringComparison.Ordinal)
                || setters.Contains("\"Right\"", StringComparison.Ordinal)
                || setters.Contains("\"Position\"", StringComparison.Ordinal))
            {
                _failed = true;

                throw Error;
            }
        }
    }

    /// <summary>Retains existing entries without incidental detection during failure and retry assertions.</summary>
    private sealed record PayloadOwnerRecoveryGraph(
        NavigationNode Node,
        EntityEntry<NavigationNode> Entry,
        NavigationPayloadOwner Previous,
        NavigationPayloadOwner Target,
        PropertyEntry<NavigationNode, int?> ForeignKey,
        PropertyEntry<NavigationNote, string> Note
    );
}
