namespace Doka.EntityFrameworkCore.NestedSet.Features.Ordering;

/// <summary>Resolves domain ordering in the database and reuses the locked subtree move algorithm.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The optional mapped scope type or the scope-free marker.</typeparam>
internal sealed class NestedSetOrderer<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;

    /// <summary>Uses the caller's existing transaction and hierarchy write lock.</summary>
    /// <param name="store">The tree-bound store with configured domain ordering.</param>
    internal NestedSetOrderer(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store
    )
    {
        _store = store;
    }

    /// <summary>Rejects an explicit placement when configuration requires canonical ordering.</summary>
    /// <param name="order">The optional configured rule.</param>
    /// <exception cref="InvalidOperationException">The rule uses strict ordering.</exception>
    internal static void RequireManualPlacement(
        NestedSetOrdering? order
    )
    {
        if (order?.Mode == NestedSetOrderMode.Strict)
        {
            throw new NestedSetException(
                NestedSetErrorCode.ManualPlacementNotAllowed,
                "Strict ordering does not allow explicit sibling placement. Use automatic insertion or MoveToAsync.");
        }
    }

    /// <summary>Repositions one saved node while preserving the relative order of every other sibling.</summary>
    /// <param name="key">The persisted node whose ordering values may have changed.</param>
    /// <param name="cancellationToken">The cancellation token for reads and structural updates.</param>
    /// <returns>The interval containing changed rows, or an empty result when the node is already ordered.</returns>
    internal async Task<IReadOnlyList<NestedSetChangedInterval>> ReorderAsync(
        TKey key,
        CancellationToken cancellationToken
    )
    {
        var source = await _store
            .FindAsync(key, cancellationToken)
            .ConfigureAwait(false);

        NestedSetGuards.RequireBounds(source);

        var destination = await ResolveAsync(key, source.Parent, cancellationToken).ConfigureAwait(false);
        var changed = await new NestedSetSubtreeMover<TEntity, TKey, TTreeId, TScope>(_store)
            .MoveAsync(source, destination, cancellationToken)
            .ConfigureAwait(false);

        // WHY: Automatic reordering stays inside the source's parent group. All crossed sibling positions
        // and moved boundaries therefore lie in this range, unlike a general cross-parent move.
        return changed
            ?
            [
                new NestedSetChangedInterval(
                    Math.Min(source.Left, destination.Boundary),
                    Math.Max(source.Right, checked(destination.Boundary - 1)))
            ]
            : [];
    }

    /// <summary>Sorts one whole sibling group using bounded interval-permutation updates.</summary>
    /// <param name="parent">The typed parent identity, or an absent value for roots.</param>
    /// <param name="cancellationToken">The token used while reading and permuting intervals.</param>
    /// <returns>The coordinate ranges containing changed rows, without loading descendant entities.</returns>
    internal Task<IReadOnlyList<NestedSetChangedInterval>> ReorderSiblingsAsync(
        NestedSetParent<TKey> parent,
        CancellationToken cancellationToken
    ) => new NestedSetSiblingReorderer<TEntity, TKey, TTreeId, TScope>(_store).ReorderAsync(parent, cancellationToken);

    /// <summary>Finds the destination group's rule predecessor using native database ordering.</summary>
    /// <param name="key">The saved source key; its payload supplies the ordering values.</param>
    /// <param name="parent">The typed destination parent, or an absent value for roots.</param>
    /// <param name="cancellationToken">The token used for the destination and window queries.</param>
    /// <param name="knownParent">The destination parent already read from this tree under the current lock.</param>
    /// <returns>A destination measured before removing the source interval.</returns>
    internal async Task<NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination> ResolveAsync(
        TKey key,
        NestedSetParent<TKey> parent,
        CancellationToken cancellationToken,
        NestedSetNode<TKey>? knownParent = null
    )
    {
        long firstBoundary = 1;
        var depth = 0;

        if (parent.HasValue)
        {
            // WHY: Moves already read this parent to reject cycles, with no intervening structural writes.
            var target = knownParent
                ?? await _store
                    .FindAsync(parent.Value, cancellationToken)
                    .ConfigureAwait(false);

            NestedSetGuards.RequireBounds(target);
            firstBoundary = checked(target.Left + 1);
            depth = checked(target.Depth + 1);
        }

        var order = _store.Map.Order
            ?? throw new InvalidOperationException("Configure a sibling ordering rule before resolving its position.");

        var sql = _store.Context.GetService<ISqlGenerationHelper>();
        var table = _store.Map.Store;
        var keyMapping = _store.Map.PropertyMapping(_store.Map.Key);
        var parentMapping = _store.Map.PropertyMapping(_store.Map.Parent);
        var keyColumn = sql.DelimitIdentifier(keyMapping.ColumnName);
        var parentColumn = sql.DelimitIdentifier(parentMapping.ColumnName);

        if (_store.Map.KeyCollation is { } collation)
        {
            // WHY: Parent aliases must use the same equality as the referenced primary key, including collation.
            parentColumn += " COLLATE "
                + NestedSetProviderCapabilities
                    .Resolve(_store.Context)
                    .CollationSql(sql, collation);
        }

        var terms = new List<string>(checked(order.Properties.Count * 2));

        for (var index = 0; index < order.Properties.Count; index++)
        {
            var column = sql.DelimitIdentifier(
                _store.Map.PropertyMapping(order.Properties[index].Name).ColumnName);

            if (order.NullSortOrders[index] is { } nullSortOrder)
            {
                // WHY: Native provider defaults disagree about null placement. This rank mirrors the LINQ
                // expression used for sibling queries and keeps window-based mutation placement deterministic.
                var nullRank = nullSortOrder == NullSortOrder.First ? 0 : 1;
                var nonNullRank = 1 - nullRank;

                terms.Add($"CASE WHEN {column} IS NULL THEN {nullRank} ELSE {nonNullRank} END ASC");
            }

            terms.Add(column + (order.Descending[index] ? " DESC" : " ASC"));
        }

        var ordering = string.Join(", ", terms);
        var rightColumn = sql.DelimitIdentifier(
            _store.Map.PropertyMapping(_store.Map.Right).ColumnName);

        var positionColumn = sql.DelimitIdentifier(
            _store.Map.PropertyMapping(_store.Map.Position).ColumnName);

        var nodeAlias = sql.DelimitIdentifier("NodeKey");
        var rightAlias = sql.DelimitIdentifier(nameof(NestedSetPredecessor.Right));
        var positionAlias = sql.DelimitIdentifier(nameof(NestedSetPredecessor.Position));
        var parameters = new List<object>();

        await using var command = _store
            .Context
            .Database
            .GetDbConnection()
            .CreateCommand();

        var identity = new List<string>();

        if (_store.Map.Scope is { } scopeName)
        {
            var scopeMapping = _store.Map.PropertyMapping(scopeName);
            identity.Add($"{sql.DelimitIdentifier(scopeMapping.ColumnName)} = {{{parameters.Count}}}");
            parameters.Add(scopeMapping.TypeMapping.CreateParameter(command, "scope", _store.Scope, nullable: false));
        }

        var treeMapping = _store.Map.PropertyMapping(_store.Map.TreeId);
        identity.Add($"{sql.DelimitIdentifier(treeMapping.ColumnName)} = {{{parameters.Count}}}");
        parameters.Add(treeMapping.TypeMapping.CreateParameter(command, "treeId", _store.TreeId, nullable: false));

        var parentIndex = parameters.Count;
        parameters.Add(parentMapping.TypeMapping.CreateParameter(command, "parent", parent.BoxedValue, nullable: true));
        var keyIndex = parameters.Count;
        parameters.Add(keyMapping.TypeMapping.CreateParameter(command, "key", key, nullable: false));
        var siblings = !parent.HasValue ? parentColumn + " IS NULL" : parentColumn + $" = {{{parentIndex}}}";
        var identityPredicate = string.Join(" AND ", identity) + " AND ";

        // WHY: The window sees the complete destination group plus the source, even for a cross-parent move.
        // Filtering the source only in the outer query preserves its actual rule predecessor and avoids CLR sorting.
        var query = $"SELECT {rightAlias}, {positionAlias} FROM ("
            + $"SELECT {keyColumn} AS {nodeAlias}, "
            + $"LAG({rightColumn}) OVER (ORDER BY {ordering}) AS {rightAlias}, "
            + $"LAG({positionColumn}) OVER (ORDER BY {ordering}) AS {positionAlias} "
            + $"FROM {sql.DelimitIdentifier(table.Name, table.Schema)} "
            + $"WHERE {identityPredicate}(({siblings}) OR {keyColumn} = {{{keyIndex}}})) "
            + $"AS {sql.DelimitIdentifier("ordered")} WHERE {nodeAlias} = {{{keyIndex}}}";

        await using (command.ConfigureAwait(false))
        {
            var previous = await _store
                .Context
                .Database
                .SqlQueryRaw<NestedSetPredecessor>(query, parameters.ToArray())
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            return new NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination(
                previous.Right is { } right ? checked(right + 1) : firstBoundary,
                parent,
                depth,
                previous.Position is { } position ? checked(position + 1) : 0);
        }
    }
}
