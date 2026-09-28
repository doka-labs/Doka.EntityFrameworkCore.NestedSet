namespace Doka.EntityFrameworkCore.NestedSet.Features.Facade;

/// <summary>Dispatches validated generic arguments through an immutable cached model specialization.</summary>
internal abstract class NestedSetMutationInvoker<TEntity>
    where TEntity : class
{
    /// <summary>Closes one invoker over the finalized NodeKey, TreeId, and optional Scope model types.</summary>
    internal static NestedSetMutationInvoker<TEntity> Create(
        NestedSetModelDescriptor descriptor
    )
    {
        var scope = descriptor.Scope?.ClrType ?? typeof(NestedSetNoScope);
        var closed = typeof(NestedSetMutationInvoker<,,,>).MakeGenericType(
            typeof(TEntity),
            descriptor.NodeKey.ClrType,
            descriptor.TreeId.ClrType,
            scope);

        return (NestedSetMutationInvoker<TEntity>)Activator.CreateInstance(closed)!;
    }

    /// <summary>Creates a tree through the exact mapped TreeId type.</summary>
    internal abstract Task InsertRootAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TEntity entity,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    )
        where TRequestedTreeId : notnull;

    /// <summary>Inserts relative to a parent or sibling resolved by its exact mapped key type.</summary>
    internal abstract Task InsertChildAsync<TRequestedKey>(
        NestedSetMutationBinding<TEntity> binding,
        TEntity entity,
        TRequestedKey parent,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    )
        where TRequestedKey : notnull;

    /// <summary>Imports a detached branch beneath an exact mapped parent key.</summary>
    internal abstract Task InsertSubtreeAsync<TRequestedKey>(
        NestedSetMutationBinding<TEntity> binding,
        NestedSetBranch<TEntity> subtree,
        TRequestedKey parent,
        CancellationToken cancellationToken
    )
        where TRequestedKey : notnull;

    /// <summary>Imports trees while preserving their validated TreeId type and source list.</summary>
    internal abstract Task InsertForestAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        IReadOnlyList<NestedSetTreeImport<TEntity, TRequestedTreeId>> trees,
        CancellationToken cancellationToken
    )
        where TRequestedTreeId : notnull;

    /// <summary>Moves a subtree relative to an exact mapped anchor key.</summary>
    internal abstract Task MoveAsync<TRequestedKey>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedKey key,
        TRequestedKey anchor,
        NestedSetPlacement placement,
        bool automatic,
        CancellationToken cancellationToken
    )
        where TRequestedKey : notnull;

    /// <summary>Reserves a typed identity and detaches a subtree as its only root.</summary>
    internal abstract Task DetachAsTreeAsync<TRequestedKey, TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedKey key,
        TRequestedTreeId newTreeId,
        CancellationToken cancellationToken
    )
        where TRequestedKey : notnull
        where TRequestedTreeId : notnull;

    /// <summary>Deletes a resolved node or subtree within its exact persisted tree.</summary>
    internal abstract Task DeleteAsync<TRequestedKey>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedKey key,
        bool subtree,
        CancellationToken cancellationToken
    )
        where TRequestedKey : notnull;

    /// <summary>Deletes every node of an explicitly identified tree.</summary>
    internal abstract Task DeleteTreeAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    )
        where TRequestedTreeId : notnull;

    /// <summary>Purges an empty tombstone selected by the exact mapped TreeId type.</summary>
    internal abstract Task PurgeTreeIdAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    )
        where TRequestedTreeId : notnull;

    /// <summary>Inspects an exact tree using the requested bounded validation level.</summary>
    internal abstract Task<NestedSetValidationReport> ValidateTreeAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        NestedSetValidationLevel level,
        CancellationToken cancellationToken
    )
        where TRequestedTreeId : notnull;

    /// <summary>Plans coordinate repairs for an exact typed tree without writing them.</summary>
    internal abstract Task<NestedSetRebuildPlan> PlanRebuildAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    )
        where TRequestedTreeId : notnull;

    /// <summary>Repairs the selected typed tree within its mutation boundary.</summary>
    internal abstract Task RebuildTreeAsync<TRequestedTreeId>(
        NestedSetMutationBinding<TEntity> binding,
        TRequestedTreeId treeId,
        CancellationToken cancellationToken
    )
        where TRequestedTreeId : notnull;
}
