namespace Doka.EntityFrameworkCore.NestedSet.Postgres;

internal sealed partial class PostgresNestedSetProvider
{
    /// <inheritdoc />
    internal override IsolationLevel RequiredIsolation => IsolationLevel.ReadCommitted;

    /// <inheritdoc />
    internal override bool SupportsStatementAtomicSelfReferentialDelete => true;

    /// <inheritdoc />
    internal override bool IsSafeReadSnapshot(
        IsolationLevel? isolation
    ) => isolation is IsolationLevel.Serializable or IsolationLevel.Snapshot or IsolationLevel.RepeatableRead;

    /// <inheritdoc />
    internal override bool HasStatementReadSnapshot(
        IsolationLevel? isolation
    ) => isolation is null or IsolationLevel.ReadCommitted;

    /// <inheritdoc />
    internal override IsolationLevel ReadSnapshotIsolation => IsolationLevel.RepeatableRead;

    /// <inheritdoc />
    internal override async Task<bool> ReserveTreeAsync(
        NestedSetTreeLock treeLock,
        CancellationToken cancellationToken
    )
    {
        var conflict = treeLock.Scope is null ? treeLock.TreeId : $"{treeLock.Scope}, {treeLock.TreeId}";
        var statement = treeLock.DirectInsertSql() + $" ON CONFLICT ({conflict}) DO NOTHING";

        return await treeLock
                .ExecuteAsync(statement, cancellationToken)
                .ConfigureAwait(false)
            == 1;
    }

    /// <inheritdoc />
    internal override string TreeLifecycleReadSql(
            NestedSetTreeLock treeLock,
            string valueAlias
        )
        // WHY: FK checks use KEY SHARE; NO KEY UPDATE still excludes hierarchy writers while allowing those checks.
        => $"SELECT {treeLock.Lifecycle} AS {valueAlias} FROM {treeLock.Table} "
            + $"WHERE {treeLock.Predicate()} FOR NO KEY UPDATE";
}
