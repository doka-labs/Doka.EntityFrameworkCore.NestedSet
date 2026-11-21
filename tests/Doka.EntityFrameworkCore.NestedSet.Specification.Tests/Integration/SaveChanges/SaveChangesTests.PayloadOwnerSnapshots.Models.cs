namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Mutates ordinary payload only after the first actual hierarchy UPDATE command returns.</summary>
    private sealed class PayloadOwnerCommandCallback : DbCommandInterceptor
    {
        /// <summary>Gets or sets the one mutation performed after the UPDATE was sent to the database.</summary>
        internal Action? OnUpdate { get; set; }

        /// <summary>Gets whether an actual payload UPDATE reached the provider-completion boundary.</summary>
        internal bool Reached { get; private set; }

        /// <inheritdoc />
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            Observe(command);

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
            Observe(command);

            return ValueTask.FromResult(result);
        }

        /// <summary>Ignores reads and later ordering commands so only the first sent-value boundary changes.</summary>
        private void Observe(
            DbCommand command
        )
        {
            var sql = command.CommandText;

            // WHY: Match the table immediately after UPDATE so a provider's SELECT FOR UPDATE cannot fire this hook.
            if (Reached
                || !sql.Contains(nameof(NavigationNode.PayloadOwnerId), StringComparison.Ordinal)
                || !(sql.Contains("UPDATE \"NavigationSaveNodes\"", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("UPDATE [NavigationSaveNodes]", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("UPDATE `NavigationSaveNodes`", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Reached = true;
            OnUpdate?.Invoke();
        }
    }
}
