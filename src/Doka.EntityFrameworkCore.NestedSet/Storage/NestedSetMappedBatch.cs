namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Executes bounded SQL with model-delimited identifiers and mapped provider parameters.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TTreeId">The exact mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The optional scope key type.</typeparam>
internal sealed class NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly ISqlGenerationHelper _sql;
    private readonly Dictionary<string, string> _columns = new(StringComparer.Ordinal);

    /// <summary>Resolves SQL identifiers once for a context-scoped structural writer.</summary>
    internal NestedSetMappedBatch(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store
    )
    {
        _store = store;
        _sql = store.Context.GetService<ISqlGenerationHelper>();
        var table = store.Map.Store;
        Table = _sql.DelimitIdentifier(table.Name, table.Schema);
        KeyBatchIdentityPredicate = CreateKeyBatchIdentityPredicate();
    }

    /// <summary>The safely delimited schema-qualified hierarchy table.</summary>
    internal string Table { get; }

    /// <summary>Restricts keyed writes to this tree without making SQLite scan the complete tree per batch.</summary>
    internal string KeyBatchIdentityPredicate { get; }

    /// <summary>Gets the mandatory optional-Scope and exact-TreeId predicate for this store.</summary>
    internal string IdentityPredicate
    {
        get
        {
            var tree = $"{Column(_store.Map.TreeId)} = {Parameter("treeId")}";

            return _store.Map.Scope is { } scope
                ? $"{Column(scope)} = {Parameter("scope")} AND {tree}"
                : tree;
        }
    }

    /// <summary>Resolves a structural property to its safely delimited column name.</summary>
    internal string Column(
        string property
    )
    {
        if (!_columns.TryGetValue(property, out var column))
        {
            column = _sql.DelimitIdentifier(_store.Map.PropertyMapping(property).ColumnName);
            _columns.Add(property, column);
        }

        return column;
    }

    /// <summary>Creates a provider-specific marker for a fixed internally generated parameter name.</summary>
    internal string Parameter(
        string name
    ) => _sql.GenerateParameterName(name);

    /// <summary>Keeps SQLite's unique-key access separate from the exact membership check.</summary>
    private string CreateKeyBatchIdentityPredicate()
    {
        if (NestedSetProviderCapabilities.Resolve(_store.Context).Kind != NestedSetProviderKind.Sqlite)
        {
            return IdentityPredicate;
        }

        // WHY: SQLite resolves identifiers without ASCII case sensitivity. An inner alias equal to the outer
        // table would shadow the correlated reference and turn the key comparison into a self-comparison.
        var memberName = string.Equals(_store.Map.Store.Name, "nestedSetMembership", StringComparison.OrdinalIgnoreCase)
            ? "nestedSetMembershipRow"
            : "nestedSetMembership";

        var member = _sql.DelimitIdentifier(memberName);
        var outer = _sql.DelimitIdentifier(_store.Map.Store.Name);
        var key = Column(_store.Map.Key);
        var tree = Column(_store.Map.TreeId);
        var membership = $"EXISTS (SELECT 1 FROM {Table} AS {member} "
            + $"WHERE {member}.{key} = {outer}.{key} AND {member}.{tree} = {Parameter("treeId")}";

        if (_store.Map.Scope is { } scopeName)
        {
            var scope = Column(scopeName);
            membership += $" AND {member}.{scope} = {Parameter("scope")})";

            // WHY: Without statistics, SQLite can choose the Scope/TreeId range index for every 64-key batch.
            // Keep Scope/NodeKey visible to the outer lookup; the correlated unique-key probe still enforces
            // exact tree membership, including database-native scope equality, before any row is modified.
            return $"{scope} = {Parameter("scope")} AND {membership}";
        }

        return membership + ")";
    }

    /// <summary>Executes through EF with command interception, logging, and caller transactions intact.</summary>
    /// <param name="sql">A template containing only delimited model identifiers and parameter markers.</param>
    /// <param name="values">Parameters paired with their exact mapped properties.</param>
    /// <param name="cancellationToken">The token used for command execution.</param>
    /// <returns>The affected row count reported by the provider.</returns>
    internal async Task<int> ExecuteAsync(
        string sql,
        IReadOnlyList<NestedSetBatchParameter> values,
        CancellationToken cancellationToken
    )
    {
        var command = _store
            .Context
            .Database
            .GetDbConnection()
            .CreateCommand();

        await using (command.ConfigureAwait(false))
        {
            var identityCount = _store.Map.Scope is null ? 1 : 2;
            var parameters = new object[values.Count + identityCount];
            var parameterIndex = 0;

            if (_store.Map.Scope is { } scopeName)
            {
                var scope = _store.Map.PropertyMapping(scopeName);
                parameters[parameterIndex++] = scope.TypeMapping.CreateParameter(
                    command,
                    "scope",
                    _store.Scope,
                    nullable: false);
            }

            var treeId = _store.Map.PropertyMapping(_store.Map.TreeId);
            parameters[parameterIndex++] = treeId.TypeMapping.CreateParameter(
                command,
                "treeId",
                _store.TreeId,
                nullable: false);

            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                var property = _store.Map.PropertyMapping(value.Property);
                parameters[index + parameterIndex] = property.TypeMapping.CreateParameter(
                    command,
                    value.Name,
                    value.Value,
                    nullable: property.Property.IsNullable);
            }

            // WHY: Parameters come from each property's relational mapping, including converters and facets.
            // Only parameter construction uses this command; EF owns execution so interception cannot be bypassed.
            var affected = await _store
                .Context
                .Database
                .ExecuteSqlRawAsync(sql, parameters, cancellationToken)
                .ConfigureAwait(false);

            NestedSetTelemetry.RecordRowsAffected(affected);
            NestedSetTelemetry.RecordBatch();

            return affected;
        }
    }
}

/// <summary>Associates an operation value with its mapped property before provider parameter construction.</summary>
/// <param name="Name">The internal parameter name referenced by the SQL template.</param>
/// <param name="Property">The EF property supplying the exact relational type mapping.</param>
/// <param name="Value">The model value to convert through the mapping.</param>
internal readonly record struct NestedSetBatchParameter(
    string Name,
    string Property,
    object? Value
);
