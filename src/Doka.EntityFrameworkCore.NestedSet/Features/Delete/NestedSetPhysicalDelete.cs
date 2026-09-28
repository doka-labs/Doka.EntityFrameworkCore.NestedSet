namespace Doka.EntityFrameworkCore.NestedSet.Features.Delete;

/// <summary>Deletes one mapped hierarchy row across every physical table fragment.</summary>
/// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <typeparam name="TScope">The optional mapped scope type or the scope-free marker.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
internal sealed class NestedSetPhysicalDelete<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    // WHY: A key-only DELETE has fewer per-row parameters than the shared structural UPDATE batch.
    private const int MaximumRows = 128;
    private const int MaximumParameters = 900;

    private static readonly ConditionalWeakTable<IEntityType, DeletePlan> s_plans = new();

    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly DeletePlan _plan;

    /// <summary>Resolves the immutable physical plan for the store's EF model.</summary>
    internal NestedSetPhysicalDelete(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store
    )
    {
        _store = store;
        _plan = s_plans.GetValue(store.Map.EntityType, entity => DeletePlan.Create(entity, store.Map.Store));
    }

    /// <summary>Gets whether EF's single-table ExecuteDelete cannot represent the mapping.</summary>
    internal bool IsRequired => _plan.IsRequired;

    /// <summary>Deletes the selected logical rows with bounded memory and provider-mapped key parameters.</summary>
    /// <param name="query">A scoped query identifying exactly the nodes to remove.</param>
    /// <param name="cancellationToken">The token used for reads and writes.</param>
    /// <returns>The number of logical hierarchy rows deleted.</returns>
    internal async Task<int> ExecuteAsync(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken
    )
    {
        var sql = _store.Context.GetService<ISqlGenerationHelper>();
        var keyProperties = _plan.KeyProperties;
        // WHY: Batches bound memory and stay below provider parameter limits, including composite keys.
        var batchSize = Math.Min(MaximumRows, MaximumParameters / keyProperties.Length);
        var affected = 0;

        while (true)
        {
            var keys = await query
                .OrderBy(node => EF.Property<long>(node, _store.Map.Left))
                .Select(_plan.KeyProjection)
                .Take(batchSize)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (keys.Length == 0)
            {
                break;
            }

            foreach (var table in _plan.Tables)
            {
                var deleted = await DeleteBatchAsync(table, keyProperties, keys, sql, cancellationToken)
                    .ConfigureAwait(false);

                if (ReferenceEquals(table, _plan.StructureTable)
                    && deleted != keys.Length)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidStructure,
                        "The physical hierarchy table changed during a mapped deletion.");
                }
            }

            affected = checked(affected + keys.Length);
            NestedSetTelemetry.RecordRowsAffected(keys.Length);
        }

        return affected;
    }

    private async Task<int> DeleteBatchAsync(
        ITable table,
        IProperty[] keyProperties,
        object?[][] keys,
        ISqlGenerationHelper sql,
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
            var statement = new StringBuilder("DELETE FROM ");
            statement.Append(sql.DelimitIdentifier(table.Name, table.Schema));
            statement.Append(" WHERE ");
            var parameters = new object[keys.Length * keyProperties.Length];
            var index = 0;

            for (var row = 0; row < keys.Length; row++)
            {
                if (row > 0)
                {
                    statement.Append(" OR ");
                }

                statement.Append('(');

                for (var propertyIndex = 0; propertyIndex < keyProperties.Length; propertyIndex++)
                {
                    if (propertyIndex > 0)
                    {
                        statement.Append(" AND ");
                    }

                    var property = keyProperties[propertyIndex];
                    var column = table.FindColumn(property)
                        ?? throw new InvalidOperationException(
                            $"Primary key '{property.Name}' is absent from table '{table.Name}'.");

                    var mapping = column.PropertyMappings.First(value => value.Property == property);
                    var name = $"key{index}";
                    statement.Append(sql.DelimitIdentifier(column.Name));
                    statement.Append(" = ");
                    statement.Append(sql.GenerateParameterName(name));
                    parameters[index++] = mapping.TypeMapping.CreateParameter(
                        command,
                        name,
                        keys[row][propertyIndex],
                        nullable: false);
                }

                statement.Append(')');
            }

            // WHY: EF owns execution and the caller's transaction; its table-specific mapping converts keys
            // correctly even when a provider stores a CLR Guid or another key in a non-default representation.
            var deleted = await _store
                .Context
                .Database
                .ExecuteSqlRawAsync(statement.ToString(), parameters, cancellationToken)
                .ConfigureAwait(false);

            NestedSetTelemetry.RecordBatch();

            return deleted;
        }
    }

    private sealed class DeletePlan
    {
        private DeletePlan(
            IProperty[] keyProperties,
            Expression<Func<TEntity, object?[]>> keyProjection,
            ITable[] tables,
            ITable structureTable,
            bool isRequired
        )
        {
            KeyProperties = keyProperties;
            KeyProjection = keyProjection;
            Tables = tables;
            StructureTable = structureTable;
            IsRequired = isRequired;
        }

        internal IProperty[] KeyProperties { get; }

        internal Expression<Func<TEntity, object?[]>> KeyProjection { get; }

        internal ITable[] Tables { get; }

        internal ITable StructureTable { get; }

        internal bool IsRequired { get; }

        internal static DeletePlan Create(
            IEntityType entity,
            StoreObjectIdentifier store
        )
        {
            var keyProperties = entity.FindPrimaryKey()?.Properties.ToArray()
                ?? throw new InvalidOperationException("A nested-set entity requires a primary key for deletion.");

            var mappedTypes = new HashSet<IEntityType>();

            for (var current = entity; current is not null; current = current.BaseType)
            {
                mappedTypes.Add(current);
            }

            foreach (var derived in entity.GetDerivedTypes())
            {
                mappedTypes.Add(derived);
            }

            var tables = mappedTypes
                .SelectMany(type => type.GetTableMappings())
                .Select(mapping => mapping.Table)
                .Distinct()
                .ToArray();

            var structureTable = tables.Single(table => table.Name == store.Name && table.Schema == store.Schema);
            var sharedMappings = tables.Length == 1
                ? tables[0]
                    .EntityTypeMappings
                    .Where(mapping => mapping.TypeBase is not IEntityType mappedType
                        || !mappedTypes.Contains(mappedType))
                    .ToArray()
                : [];

            var isSharedPrincipal = tables.Length == 1
                && tables[0]
                    .EntityTypeMappings
                    .Any(mapping => mapping.TypeBase is IEntityType mappedType
                        && mappedTypes.Contains(mappedType)
                        && mapping.IsSharedTablePrincipal == true);

            // WHY: EF permits ExecuteDelete for an owner's inline owned values. Another non-owned entity
            // shares an independent model identity, so its row requires the mapping-aware delete path.
            var isRequired = tables.Length > 1
                || sharedMappings.Any(mapping => mapping.TypeBase is not IEntityType type || !type.IsOwned())
                || (sharedMappings.Length > 0 && !isSharedPrincipal);

            var ordered = OrderTables(tables);
            var parameter = Expression.Parameter(typeof(TEntity), "node");
            var values = keyProperties
                .Select(property => Expression.Convert(
                    Expression.Call(
                        typeof(EF),
                        nameof(EF.Property),
                        [property.ClrType],
                        parameter,
                        Expression.Constant(property.Name)),
                    typeof(object)))
                .ToArray<Expression>();

            var projection = Expression.Lambda<Func<TEntity, object?[]>>(
                Expression.NewArrayInit(typeof(object), values),
                parameter);

            return new DeletePlan(keyProperties, projection, ordered, structureTable, isRequired);
        }

        private static ITable[] OrderTables(
            ITable[] tables
        )
        {
            var pending = new HashSet<ITable>(tables);
            var result = new List<ITable>(tables.Length);

            while (pending.Count > 0)
            {
                var next = pending.FirstOrDefault(table => !pending.Any(other =>
                    !ReferenceEquals(other, table)
                    && other.ForeignKeyConstraints.Any(foreignKey => ReferenceEquals(
                        foreignKey.PrincipalTable,
                        table))));

                if (next is null)
                {
                    throw new InvalidOperationException("The mapped hierarchy tables contain a foreign-key cycle.");
                }

                result.Add(next);
                pending.Remove(next);
            }

            // WHY: Removing dependent fragments first works even when the application overrides cascade deletes.
            return result.ToArray();
        }
    }
}
