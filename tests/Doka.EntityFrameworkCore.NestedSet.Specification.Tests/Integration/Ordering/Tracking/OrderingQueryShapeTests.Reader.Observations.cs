namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingQueryShapeTests
{
    /// <summary>Records executed scalar ordinals without changing provider services or query expressions.</summary>
    private sealed class OrdinalProbe : DbCommandInterceptor
    {
        /// <summary>Gets reader observations in actual command execution order.</summary>
        internal List<OrdinalRead> Readers { get; } = [];

        /// <summary>Starts a fresh observation window without changing the underlying EF cache.</summary>
        internal void Reset() => Readers.Clear();

        /// <inheritdoc />
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (eventData.CommandSource != CommandSource.LinqQuery)
            {
                // WHY: Npgsql's native SaveChanges batch requires its concrete reader; only LINQ refresh is observed.
                return ValueTask.FromResult(result);
            }

            var observed = new OrdinalRead(command.CommandText);
            Readers.Add(observed);

            return ValueTask.FromResult<DbDataReader>(new OrdinalReader(result, observed));
        }
    }

    /// <summary>
    /// Retains executed SQL and actual returned ordinal values after the provider command is disposed.
    /// </summary>
    private sealed class OrdinalRead
    {
        /// <summary>Retains the supplied fixture values without changing their identity.</summary>
        /// <param name="sql">The exact statement associated with this observation.</param>
        public OrdinalRead(
            string sql
        )
        {
            Sql = sql;
        }

        /// <summary>
        /// Gets the exact executed command text used to correlate this reader with the compilation probe.
        /// </summary>
        internal string Sql { get; }

        /// <summary>Gets observed numeric values from the projected first column, preserving duplicates.</summary>
        internal List<int> Ordinals { get; } = [];
    }
}
