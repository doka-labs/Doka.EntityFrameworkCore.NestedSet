namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Owns exact-tree structural queries and immediate parameterized writes.</summary>
/// <typeparam name="TEntity">The mapped domain entity.</typeparam>
/// <typeparam name="TKey">The node key type.</typeparam>
/// <typeparam name="TTreeId">The mandatory mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The optional scope type, or the scopeless marker.</typeparam>
internal sealed partial class NestedSetStore<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly TScope _scope;
    private readonly TTreeId _treeId;
    private IQueryable<TEntity>? _nodes;

    /// <summary>Binds structural operations to one resolved mapping and stable tree identity.</summary>
    /// <param name="context">The caller-owned context.</param>
    /// <param name="entityType">The exact ordinary or named shared hierarchy mapping.</param>
    /// <param name="scope">The mapped scope value, or the scopeless marker.</param>
    /// <param name="treeId">The non-null tree identity used by every structural query.</param>
    /// <exception cref="ArgumentException">
    ///     The entity mapping or generic tree type does not match the context model.
    /// </exception>
    internal NestedSetStore(
        DbContext context,
        IEntityType entityType,
        TScope scope,
        TTreeId treeId
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entityType);

        if (!ReferenceEquals(entityType.Model, context.Model)
            || entityType.ClrType != typeof(TEntity))
        {
            throw new ArgumentException(
                "The exact entity mapping must match the context model and entity type.",
                nameof(entityType));
        }

        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(treeId);
        Context = context;
        Map = NestedSetMapping<TEntity, TKey, TScope>.For(context, entityType);

        if (Map.TreeIdProperty.ClrType != typeof(TTreeId))
        {
            throw new ArgumentException(
                $"TreeId must have model type '{Map.TreeIdProperty.ClrType.FullName}', "
                + $"but the store uses '{typeof(TTreeId).FullName}'.",
                nameof(treeId));
        }

        // WHY: Caller-owned mutable identities must not change the selected tree while asynchronous work runs.
        // The shared typed helper confines EF metadata erasure to its actual framework read/snapshot boundary.
        _treeId = NestedSetTypedValue<TTreeId>.Snapshot(Map.TreeIdProperty, treeId);
        _scope = Map.ScopeProperty is { } scopeProperty
            ? NestedSetTypedValue<TScope>.Snapshot(scopeProperty, scope)
            : scope;
    }

    /// <summary>Gets the caller-owned context used by tracking and transaction coordination.</summary>
    internal DbContext Context { get; }

    /// <summary>Gets immutable metadata shared only by stores using this exact mapping.</summary>
    internal NestedSetMapping<TEntity, TKey, TScope> Map { get; }

    /// <summary>Gets the correctly named EF set for this exact hierarchy mapping.</summary>
    internal DbSet<TEntity> Set => NestedSetEntityAccess<TEntity>.Set(Context, Map.EntityType);

    /// <summary>Returns an unambiguous entry for a detached or tracked hierarchy entity.</summary>
    /// <param name="entity">The hierarchy entity using this store's exact mapping.</param>
    /// <returns>The corresponding EF entry.</returns>
    internal EntityEntry<TEntity> Entry(
        TEntity entity
    ) => NestedSetEntityAccess<TEntity>.Entry(Context, Map.EntityType, entity);

    /// <summary>Gets the snapshotted scope assigned to inserted entities.</summary>
    internal TScope Scope => _scope;

    /// <summary>Gets the snapshotted mandatory tree identity.</summary>
    internal TTreeId TreeId => _treeId;

    /// <summary>Builds the registry request for this exact tree.</summary>
    /// <param name="mode">The required lifecycle transition.</param>
    /// <returns>The complete optional scope and mandatory tree identity.</returns>
    internal NestedSetTreeLockRequest<TTreeId, TScope> LockRequest(
        NestedSetTreeLockMode mode
    ) => new(Map.EntityType, _scope, _treeId, mode);

    /// <summary>Executes a mutation under this exact tree's registry lock and rollback boundary.</summary>
    /// <param name="executor">The transaction coordinator.</param>
    /// <param name="operation">The structural work performed after acquiring the lock.</param>
    /// <param name="mode">The required tree lifecycle.</param>
    /// <param name="cancellationToken">The token used for forward progress.</param>
    /// <returns>The complete transaction or caller savepoint operation.</returns>
    internal Task ExecuteMutationAsync(
        NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> executor,
        Func<CancellationToken, Task> operation,
        NestedSetTreeLockMode mode,
        CancellationToken cancellationToken
    ) => executor.ExecuteAsync(operation, [LockRequest(mode)], cancellationToken);
}
