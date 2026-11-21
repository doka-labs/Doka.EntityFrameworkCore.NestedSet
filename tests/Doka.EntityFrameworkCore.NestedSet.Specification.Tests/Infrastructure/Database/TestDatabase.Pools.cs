using System.Runtime.ExceptionServices;
using MySqlConnector;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TestDatabase
{
    private HashSet<string>? _knownPoolStrings;
    private HashSet<string>? _ownedMySqlPools;
    private readonly Lock _poolGate = new();

    /// <summary>Records an exact owned pool before Open can hide its authentication information.</summary>
    private void ObserveMySqlPool(
        DbConnection connection
    )
    {
        if (connection is not MySqlConnection)
        {
            return;
        }

        var connectionString = connection.ConnectionString;

        lock (_poolGate)
        {
            if (_knownPoolStrings?.Contains(connectionString) == true)
            {
                return;
            }

            var actual = new MySqlConnectionStringBuilder(connectionString);
            var configured = RelationalOptionsExtension.Extract(_options);
            var ownedString = configured.ConnectionString ?? configured.Connection?.ConnectionString
                ?? throw new InvalidOperationException("The database fixture has no owned connection identity.");

            var owned = new MySqlConnectionStringBuilder(ownedString);

            // WHY: Provider database creation may open an administrative connection. Its pool, or another
            // database's pool, does not belong to this fixture even when it passed through the same interceptor.
            if (actual.Server != owned.Server || actual.Port != owned.Port
                || actual.UserID != owned.UserID || actual.Database != owned.Database)
            {
                return;
            }

            _ownedMySqlPools ??= new HashSet<string>(StringComparer.Ordinal);
            _ownedMySqlPools.Add(connectionString);
            _knownPoolStrings ??= new HashSet<string>(StringComparer.Ordinal);
            _knownPoolStrings.Add(connectionString);

            // WHY: Open may suppress the password in the public string. Recognize that alias on later opens
            // without reparsing settings, retaining connections, or replacing the full pool identity to clear.
            actual.Remove("Password");
            _knownPoolStrings.Add(actual.ConnectionString);
        }
    }

    /// <summary>
    /// Clears every pool actually owned by this fixture after its contexts have returned their sessions.
    /// </summary>
    private async Task ClearOwnedMySqlPoolsAsync()
    {
        string[] pools;

        lock (_poolGate)
        {
            pools = _ownedMySqlPools?.ToArray() ?? [];
        }

        List<Exception>? failures = null;

        foreach (var pool in pools)
        {
            try
            {
                await using var connection = new MySqlConnection(pool);
                await MySqlConnection.ClearPoolAsync(connection, CancellationToken.None);
            }
            catch (Exception failure)
            {
                failures ??= [];
                failures.Add(failure);
            }
        }

        if (failures is null)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException("Owned database connection-pool cleanup failed.", failures);
    }

    /// <summary>Observes initializer and explicitly reconfigured pools before their first native open.</summary>
    private sealed class OwnedPoolObserver : DbConnectionInterceptor
    {
        private readonly TestDatabase _owner;

        /// <summary>Uses only this database's bounded pool ownership state.</summary>
        internal OwnedPoolObserver(
            TestDatabase owner
        )
        {
            _owner = owner;
        }

        /// <inheritdoc />
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            _owner.ObserveMySqlPool(connection);

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            _owner.ObserveMySqlPool(connection);

            return ValueTask.FromResult(result);
        }
    }
}
