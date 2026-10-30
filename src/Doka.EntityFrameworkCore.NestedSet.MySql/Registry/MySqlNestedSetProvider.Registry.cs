namespace Doka.EntityFrameworkCore.NestedSet.MySql;

internal sealed partial class MySqlNestedSetProvider
{
    /// <inheritdoc />
    internal override IsolationLevel RequiredIsolation => IsolationLevel.ReadCommitted;

    /// <inheritdoc />
    internal override bool SupportsStatementAtomicSelfReferentialDelete => false;

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
        // WHY: Doka requires matched-row reporting. A revision marker distinguishes insert from duplicate
        // without relying on the affected count, and the rejected reservation rolls that marker back.
        var insert = treeLock.DirectInsertSql()
            + $" ON DUPLICATE KEY UPDATE {treeLock.Revision} = {treeLock.Revision} + 1";

        await treeLock
            .ExecuteAsync(insert, cancellationToken)
            .ConfigureAwait(false);

        var value = treeLock
            .Context
            .GetService<ISqlGenerationHelper>()
            .DelimitIdentifier("Value");

        var statement = $"SELECT {treeLock.Revision} AS {value} FROM {treeLock.Table} "
            + $"WHERE {treeLock.Predicate()} FOR UPDATE";

        var revisions = await treeLock
            .Context
            .Database
            .SqlQueryRaw<long>(statement, treeLock.CreateParameters())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return revisions.Count == 1 && revisions[0] == 0;
    }

    /// <inheritdoc />
    internal override string TreeLifecycleReadSql(
        NestedSetTreeLock treeLock,
        string valueAlias
    ) => $"SELECT {treeLock.Lifecycle} AS {valueAlias} FROM {treeLock.Table} "
        + $"WHERE {treeLock.Predicate()} FOR UPDATE";
}
