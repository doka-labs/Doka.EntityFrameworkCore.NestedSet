namespace Doka.EntityFrameworkCore.NestedSet.Sqlite;

internal sealed partial class SqliteNestedSetProvider
{
    /// <inheritdoc />
    internal override IsolationLevel RequiredIsolation => IsolationLevel.Serializable;

    /// <inheritdoc />
    internal override bool SupportsStatementAtomicSelfReferentialDelete => false;

    /// <inheritdoc />
    internal override bool MeasureTreeRowLockWait => false;

    /// <inheritdoc />
    internal override bool MeasureTransactionBeginLockWait => true;

    /// <inheritdoc />
    internal override bool IsSafeReadSnapshot(
        IsolationLevel? isolation
    ) => isolation is IsolationLevel.Serializable or IsolationLevel.Snapshot;

    /// <inheritdoc />
    internal override bool HasStatementReadSnapshot(
        IsolationLevel? isolation
    ) => isolation is null;

    /// <inheritdoc />
    internal override IsolationLevel ReadSnapshotIsolation => IsolationLevel.Serializable;

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
    internal override async Task LockExistingTreeAsync(
        NestedSetTreeLock treeLock,
        CancellationToken cancellationToken
    )
    {
        // WHY: A SELECT cannot acquire the writer lock. This no-op enters the same writer transaction
        // as later structure statements without changing the committed registry revision.
        var statement = $"UPDATE {treeLock.Table} SET {treeLock.Revision} = {treeLock.Revision} "
            + $"WHERE {treeLock.Predicate()}";

        await treeLock
            .ExecuteAsync(statement, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    internal override string TreeLifecycleReadSql(
        NestedSetTreeLock treeLock,
        string valueAlias
    ) => $"SELECT {treeLock.Lifecycle} AS {valueAlias} FROM {treeLock.Table} WHERE {treeLock.Predicate()}";

    /// <inheritdoc />
    internal override void ValidateOrdering(
        NestedSetOrdering? order
    )
    {
        if (order is null)
        {
            return;
        }

        foreach (var property in order.Properties)
        {
            var providerType = property.GetTypeMapping()
                    .Converter?.ProviderClrType
                ?? property.ClrType;
            providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;

            // WHY: EF orders decimals with EF_DECIMAL while mapped command windows do not inject it.
            // Every criterion needs the same ordering in EF queries and structural command windows.
            if (providerType == typeof(decimal)
                || providerType == typeof(DateTimeOffset)
                || providerType == typeof(TimeSpan)
                || providerType == typeof(ulong))
            {
                throw new InvalidOperationException(
                    $"Nested-set ordering on SQLite does not support '{property.Name}' "
                    + $"with provider type '{providerType.Name}'. "
                    + "Configure an explicit converter to a supported storage type.");
            }
        }
    }

    /// <inheritdoc />
    internal override string KeyBatchIdentityPredicate<TEntity, TKey, TTreeId, TScope>(
        NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope> batch
    )
    {
        var store = batch.Store;
        var sql = batch.Sql;

        // WHY: Identifiers are case-insensitive. An alias equal to the outer table would shadow the
        // correlated key reference and turn the membership barrier into a self-comparison.
        var memberName = string.Equals(store.Map.Store.Name, "nestedSetMembership", StringComparison.OrdinalIgnoreCase)
            ? "nestedSetMembershipRow"
            : "nestedSetMembership";

        var member = sql.DelimitIdentifier(memberName);
        var outer = sql.DelimitIdentifier(store.Map.Store.Name);
        var key = batch.Column(store.Map.Key);
        var tree = batch.Column(store.Map.TreeId);
        var membership = $"EXISTS (SELECT 1 FROM {batch.Table} AS {member} "
            + $"WHERE {member}.{key} = {outer}.{key} AND {member}.{tree} = {batch.Parameter("treeId")}";

        if (store.Map.Scope is { } scopeName)
        {
            var scope = batch.Column(scopeName);
            membership += $" AND {member}.{scope} = {batch.Parameter("scope")})";

            // WHY: Keep Scope/NodeKey visible to the outer lookup instead of scanning the whole tree
            // for each small key batch; the correlated unique-key read still enforces native membership.
            return $"{scope} = {batch.Parameter("scope")} AND {membership}";
        }

        return membership + ")";
    }
}
