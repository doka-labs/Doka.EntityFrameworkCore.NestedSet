namespace Doka.EntityFrameworkCore.NestedSet.Features.Facade;

/// <summary>Retains validated public-facade state and dispatches mutations to exact model types.</summary>
/// <typeparam name="TEntity">The configured hierarchy entity.</typeparam>
internal sealed class NestedSetMutationBinding<TEntity>
    where TEntity : class
{
    private static readonly ConditionalWeakTable<IEntityType, Lazy<NestedSetMutationInvoker<TEntity>>> s_invokers =
        new();

    private readonly NestedSetMutationInvoker<TEntity> _invoker;
    private readonly ScopeValue? _scope;

    /// <summary>Creates an unbound mutation facade and validates the finalized hierarchy metadata.</summary>
    /// <param name="context">The caller-owned context.</param>
    /// <param name="entityType">The exact ordinary or named shared hierarchy entity type.</param>
    internal NestedSetMutationBinding(
        DbContext context,
        IEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entityType);
        Context = context;
        EntityType = entityType;

        Descriptor = NestedSetModelMapping
            .For(context.Model)
            .Descriptor(EntityType);

        // WHY: Concurrent first use may create several weak-table candidates. Only the retained Lazy closes
        // the mapped generic types, and its factory captures immutable metadata rather than a pooled context.
        _invoker = s_invokers.GetValue(
                EntityType,
                static metadata => new Lazy<NestedSetMutationInvoker<TEntity>>(
                    () => NestedSetMutationInvoker<TEntity>.Create(
                        NestedSetModelMapping
                            .For(metadata.Model)
                            .Descriptor(metadata)),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    /// <summary>Copies an already validated binding with one immutable Scope value.</summary>
    private NestedSetMutationBinding(
        NestedSetMutationBinding<TEntity> source,
        ScopeValue scope
    )
    {
        Context = source.Context;
        EntityType = source.EntityType;
        Descriptor = source.Descriptor;
        _scope = scope;
        _invoker = source._invoker;
    }

    /// <summary>Gets the caller-owned context.</summary>
    internal DbContext Context { get; }

    /// <summary>Gets the finalized hierarchy entity metadata.</summary>
    internal IEntityType EntityType { get; }

    /// <summary>Gets the immutable structural-role descriptor.</summary>
    internal NestedSetModelDescriptor Descriptor { get; }

    /// <summary>Gets whether a required Scope was bound.</summary>
    internal bool ScopeBound => Descriptor.Scope is null || _scope is not null;

    /// <summary>Binds the exact mapped Scope type and snapshots mutable values.</summary>
    internal NestedSetMutationBinding<TEntity> ForScope<TScope>(
        TScope scope
    )
        where TScope : notnull
    {
        var descriptor = Descriptor.Scope
            ?? throw new InvalidOperationException(
                $"Hierarchy '{EntityType.Name}' has no Scope. Do not call ForScope().");

        RequireType<TScope>(descriptor, NestedSetPropertyRoles.Scope);
        ArgumentNullException.ThrowIfNull(scope);
        var property = descriptor.Resolve(EntityType);
        var snapshot = NestedSetTypedValue<TScope>.Snapshot(property, scope);

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        return new NestedSetMutationBinding<TEntity>(this, new ScopeValue<TScope>(snapshot));
    }

    /// <summary>Reads the snapshotted typed Scope or the zero-sized marker of a scopeless hierarchy.</summary>
    internal TScope GetScope<TScope>()
        where TScope : notnull
    {
        RequireScope();

        if (Descriptor.Scope is null)
        {
            if (typeof(TScope) != typeof(NestedSetNoScope))
            {
                throw new ArgumentException(
                    "A scopeless hierarchy requires the internal scopeless marker.",
                    nameof(TScope));
            }

            return default!;
        }

        // WHY: The model specialization fixes the exact Scope type, and ForScope checked it before binding.
        // Cast the carrier reference so value-type scopes remain typed after their single metadata snapshot.
        return ((ScopeValue<TScope>)_scope!).Value;
    }

    /// <summary>Creates one new tree and inserts its only root.</summary>
    internal Task InsertRootAsync<TTreeId>(
        TEntity entity,
        TTreeId treeId,
        CancellationToken cancellationToken
    )
        where TTreeId : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(treeId);
        RequireType<TTreeId>(Descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        return _invoker.InsertRootAsync(this, entity, treeId, cancellationToken);
    }

    /// <summary>Inserts a node using the tree identity of its parent.</summary>
    internal Task InsertChildAsync<TKey>(
        TEntity entity,
        TKey parent,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(parent);
        RequireType<TKey>(Descriptor.NodeKey, NestedSetPropertyRoles.NodeKey);
        RequireManualPlacement(automatic);

        return _invoker.InsertChildAsync(this, entity, parent, placement, automatic, cancellationToken);
    }

    /// <summary>Imports a complete detached branch beneath a database-resolved parent.</summary>
    internal Task InsertSubtreeAsync<TKey>(
        NestedSetBranch<TEntity> subtree,
        TKey parent,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(subtree);
        ArgumentNullException.ThrowIfNull(parent);
        RequireType<TKey>(Descriptor.NodeKey, NestedSetPropertyRoles.NodeKey);

        return _invoker.InsertSubtreeAsync(this, subtree, parent, cancellationToken);
    }

    /// <summary>Imports independently identified root branches in one atomic forest operation.</summary>
    internal Task InsertForestAsync<TTreeId>(
        IReadOnlyList<NestedSetTreeImport<TEntity, TTreeId>> trees,
        CancellationToken cancellationToken
    )
        where TTreeId : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(trees);
        RequireType<TTreeId>(Descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        return _invoker.InsertForestAsync(this, trees, cancellationToken);
    }

    /// <summary>Moves one subtree relative to a database-resolved parent or sibling.</summary>
    internal Task MoveAsync<TKey>(
        TKey key,
        TKey anchor,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(anchor);
        RequireType<TKey>(Descriptor.NodeKey, NestedSetPropertyRoles.NodeKey);
        RequireManualPlacement(automatic);

        return _invoker.MoveAsync(this, key, anchor, placement, automatic, cancellationToken);
    }

    /// <summary>Detaches one subtree under a newly reserved TreeId.</summary>
    internal Task DetachAsTreeAsync<TKey, TTreeId>(
        TKey key,
        TTreeId newTreeId,
        CancellationToken cancellationToken
    )
        where TKey : notnull
        where TTreeId : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(newTreeId);
        RequireType<TKey>(Descriptor.NodeKey, NestedSetPropertyRoles.NodeKey);
        RequireType<TTreeId>(Descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        return _invoker.DetachAsTreeAsync(this, key, newTreeId, cancellationToken);
    }

    /// <summary>Deletes one node or its complete subtree after resolving its exact TreeId.</summary>
    internal Task DeleteAsync<TKey>(
        TKey key,
        bool subtree,
        CancellationToken cancellationToken
    )
        where TKey : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(key);
        RequireType<TKey>(Descriptor.NodeKey, NestedSetPropertyRoles.NodeKey);

        return _invoker.DeleteAsync(this, key, subtree, cancellationToken);
    }

    /// <summary>Deletes every node in one explicit tree and retires its TreeId.</summary>
    internal Task DeleteTreeAsync<TTreeId>(
        TTreeId treeId,
        CancellationToken cancellationToken
    )
        where TTreeId : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(treeId);
        RequireType<TTreeId>(Descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        return _invoker.DeleteTreeAsync(this, treeId, cancellationToken);
    }

    /// <summary>Removes one empty tombstone so deliberate TreeId reuse becomes possible.</summary>
    internal Task PurgeTreeIdAsync<TTreeId>(
        TTreeId treeId,
        CancellationToken cancellationToken
    )
        where TTreeId : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(treeId);
        RequireType<TTreeId>(Descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        return _invoker.PurgeTreeIdAsync(this, treeId, cancellationToken);
    }

    /// <summary>Validates one exact tree selected by the target facade.</summary>
    internal Task<NestedSetValidationReport> ValidateTreeAsync<TTreeId>(
        TTreeId treeId,
        NestedSetValidationLevel level,
        CancellationToken cancellationToken
    )
        where TTreeId : notnull
    {
        RequireScope();
        ArgumentNullException.ThrowIfNull(treeId);
        RequireType<TTreeId>(Descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        return _invoker.ValidateTreeAsync(this, treeId, level, cancellationToken);
    }

    /// <summary>Builds a write-free repair plan for one exact tree.</summary>
    internal Task<NestedSetRebuildPlan> PlanRebuildAsync<TTreeId>(
        TTreeId treeId,
        CancellationToken cancellationToken
    )
        where TTreeId : notnull
    {
        RequireScope();
        ArgumentNullException.ThrowIfNull(treeId);
        RequireType<TTreeId>(Descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        return _invoker.PlanRebuildAsync(this, treeId, cancellationToken);
    }

    /// <summary>Rebuilds one exact tree from its stored adjacency and sibling ordering.</summary>
    internal Task RebuildTreeAsync<TTreeId>(
        TTreeId treeId,
        CancellationToken cancellationToken
    )
        where TTreeId : notnull
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(treeId);
        RequireType<TTreeId>(Descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        return _invoker.RebuildTreeAsync(this, treeId, cancellationToken);
    }

    /// <summary>Requires a bound Scope and the registered persistence protocol before structural writes.</summary>
    private void RequireReady()
    {
        RequireScope();

        // WHY: Typed registry metadata is produced by options registration. Reject missing integration through
        // the existing public error contract before constructing a store or attempting any lock or payload write.
        NestedSetSaveChanges.RequireConfigured(Context);
    }

    /// <summary>Requires a bound Scope for both consistent inspection and structural writes.</summary>
    private void RequireScope()
    {
        if (!ScopeBound)
        {
            throw new InvalidOperationException(
                $"Hierarchy '{EntityType.Name}' defines Scope '{Descriptor.Scope!.Value.Name}'. "
                + "Call ForScope() first.");
        }
    }

    /// <summary>Rejects explicit placement before resolving any node or tree identity.</summary>
    private void RequireManualPlacement(
        bool automatic
    )
    {
        if (!automatic
            && Descriptor.OrderMode == NestedSetOrderMode.Strict)
        {
            throw new NestedSetException(
                NestedSetErrorCode.ManualPlacementNotAllowed,
                "Strict ordering does not allow explicit sibling placement. Use automatic insertion or MoveToAsync.");
        }
    }

    /// <summary>Rejects generic arguments that differ from the finalized mapped property type.</summary>
    private void RequireType<TValue>(
        NestedSetPropertyDescriptor property,
        string role
    )
        where TValue : notnull
    {
        if (property.ClrType != typeof(TValue))
        {
            throw new ArgumentException(
                $"{role} for hierarchy '{EntityType.Name}' has model type '{property.ClrType.FullName}', "
                + $"but the argument has type '{typeof(TValue).FullName}'.",
                role);
        }
    }

    /// <summary>Holds a reference to one immutable typed Scope without erasing its scalar value.</summary>
    private abstract class ScopeValue;

    /// <summary>Retains the Scope snapshot created once by the public binding operation.</summary>
    /// <typeparam name="TScope">The exact mapped Scope type validated by ForScope.</typeparam>
    private sealed class ScopeValue<TScope> : ScopeValue
        where TScope : notnull
    {
        /// <summary>Retains the independent typed snapshot without additional copying or conversion.</summary>
        internal ScopeValue(
            TScope value
        )
        {
            Value = value;
        }

        /// <summary>Gets the immutable typed Scope snapshot.</summary>
        internal TScope Value { get; }
    }
}
