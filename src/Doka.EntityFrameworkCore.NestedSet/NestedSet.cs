namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Provides composable hierarchy queries and operations over a normal EF Core entity set.</summary>
/// <typeparam name="TEntity">The configured hierarchy entity.</typeparam>
public sealed class NestedSet<TEntity>
    where TEntity : class
{
    private readonly NestedSetQuery<TEntity> _query;
    private readonly NestedSetMutationBinding<TEntity> _mutations;

    /// <summary>Creates the public facade after immediate finalized-model validation.</summary>
    /// <param name="context">The caller-owned context.</param>
    /// <param name="entityTypeName">The shared entity-type name, or null for an ordinary CLR entity type.</param>
    internal NestedSet(
        DbContext context,
        string? entityTypeName = null
    )
    {
        var entityType = NestedSetEntityAccess<TEntity>.Resolve(context, entityTypeName);

        _query = new NestedSetQuery<TEntity>(context, entityType);
        _mutations = new NestedSetMutationBinding<TEntity>(context, entityType);
    }

    /// <summary>Binds the application partition required by a scoped hierarchy.</summary>
    /// <typeparam name="TScope">The exact mapped Scope type inferred from <paramref name="scope" />.</typeparam>
    /// <param name="scope">The non-null partition value.</param>
    /// <returns>A facade who's every query and operation includes the Scope predicate.</returns>
    /// <exception cref="InvalidOperationException">The hierarchy has no configured Scope.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TScope" /> differs from the mapped Scope type.</exception>
    public ScopedNestedSet<TEntity, TScope> ForScope<TScope>(
        TScope scope
    )
        where TScope : notnull => new(_query.ForScope(scope), _mutations.ForScope(scope));

    /// <summary>Selects one complete tree in stable nested-set preorder.</summary>
    /// <typeparam name="TTreeId">The exact mapped TreeId type inferred from <paramref name="treeId" />.</typeparam>
    /// <param name="treeId">The stable tree identity.</param>
    /// <returns>A tree-bound facade whose <see cref="NestedSetTree{TEntity, TTreeId}.Nodes" /> query is composable.</returns>
    /// <exception cref="InvalidOperationException">The hierarchy defines a Scope that has not been bound.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TTreeId" /> differs from the mapped TreeId type.</exception>
    public NestedSetTree<TEntity, TTreeId> InTree<TTreeId>(
        TTreeId treeId
    )
        where TTreeId : notnull => _query.InTree(treeId, _mutations);

    /// <summary>Returns the complete tree containing the visible anchor without loading it first.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type inferred from <paramref name="nodeKey" />.</typeparam>
    /// <param name="nodeKey">The visible anchor identity.</param>
    /// <returns>A composable no-tracking query in preorder, or an empty query when the anchor is absent or filtered.</returns>
    /// <exception cref="InvalidOperationException">The hierarchy defines a Scope that has not been bound.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey" /> differs from the mapped NodeKey type.</exception>
    public IQueryable<TEntity> TreeContaining<TKey>(
        TKey nodeKey
    )
        where TKey : notnull => _query.TreeContaining(nodeKey);

    /// <summary>Returns the visible anchor and its visible descendants without loading the anchor first.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type inferred from <paramref name="nodeKey" />.</typeparam>
    /// <param name="nodeKey">The visible subtree anchor identity.</param>
    /// <returns>A composable no-tracking query in preorder, or an empty query when the anchor is absent or filtered.</returns>
    /// <exception cref="InvalidOperationException">The hierarchy defines a Scope that has not been bound.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey" /> differs from the mapped NodeKey type.</exception>
    public IQueryable<TEntity> SubtreeOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull => _query.SubtreeOf(nodeKey);

    /// <summary>Returns the visible direct children of an anchor without loading the anchor first.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type inferred from <paramref name="nodeKey" />.</typeparam>
    /// <param name="nodeKey">The visible parent identity.</param>
    /// <returns>A composable no-tracking query in sibling order, or an empty query when the anchor is absent or filtered.</returns>
    /// <exception cref="InvalidOperationException">The hierarchy defines a Scope that has not been bound.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey" /> differs from the mapped NodeKey type.</exception>
    public IQueryable<TEntity> ChildrenOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull => _query.ChildrenOf(nodeKey);

    /// <summary>Returns the visible strict descendants of an anchor without loading the anchor first.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type inferred from <paramref name="nodeKey" />.</typeparam>
    /// <param name="nodeKey">The visible ancestor identity.</param>
    /// <returns>A composable no-tracking query in preorder, or an empty query when the anchor is absent or filtered.</returns>
    /// <exception cref="InvalidOperationException">The hierarchy defines a Scope that has not been bound.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey" /> differs from the mapped NodeKey type.</exception>
    public IQueryable<TEntity> DescendantsOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull => _query.DescendantsOf(nodeKey);

    /// <summary>Returns the visible strict ancestors of an anchor without loading the anchor first.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type inferred from <paramref name="nodeKey" />.</typeparam>
    /// <param name="nodeKey">The visible descendant identity.</param>
    /// <returns>A composable no-tracking query from root to parent, or an empty query when the anchor is absent or filtered.</returns>
    /// <exception cref="InvalidOperationException">The hierarchy defines a Scope that has not been bound.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey" /> differs from the mapped NodeKey type.</exception>
    public IQueryable<TEntity> AncestorsOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull => _query.AncestorsOf(nodeKey);

    /// <summary>Returns the visible direct parent of an anchor without loading the anchor first.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type inferred from <paramref name="nodeKey" />.</typeparam>
    /// <param name="nodeKey">The visible child identity.</param>
    /// <returns>A composable no-tracking query containing at most one parent.</returns>
    /// <exception cref="InvalidOperationException">The hierarchy defines a Scope that has not been bound.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey" /> differs from the mapped NodeKey type.</exception>
    public IQueryable<TEntity> ParentOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull => _query.ParentOf(nodeKey);

    /// <summary>Creates a new tree and inserts its only root under a never-before-used TreeId.</summary>
    /// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
    /// <param name="entity">The detached root entity.</param>
    /// <param name="treeId">The stable identity reserved for the new tree.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the atomic mutation boundary.</returns>
    public Task InsertRootAsync<TTreeId>(
        TEntity entity,
        TTreeId treeId,
        CancellationToken cancellationToken = default
    )
        where TTreeId : notnull => _mutations.InsertRootAsync(entity, treeId, cancellationToken);

    /// <summary>Inserts a detached node under a parent using configured sibling ordering.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="entity">The detached node to insert.</param>
    /// <param name="parentNodeKey">The parent whose Scope and TreeId are resolved in the database.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the atomic mutation boundary.</returns>
    public Task InsertChildAsync<TKey>(
        TEntity entity,
        TKey parentNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.InsertChildAsync(
        entity,
        parentNodeKey,
        NestedSetPlacement.LastChild,
        automatic: true,
        cancellationToken);

    /// <summary>Imports a complete detached branch beneath an existing parent in one atomic operation.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="subtree">The immutable adjacency branch to import.</param>
    /// <param name="parentNodeKey">The parent whose Scope and TreeId are resolved in the database.</param>
    /// <param name="cancellationToken">The token used for validation, locking, and bounded database work.</param>
    /// <returns>A task that completes after the complete subtree is visible.</returns>
    public Task InsertSubtreeAsync<TKey>(
        NestedSetBranch<TEntity> subtree,
        TKey parentNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.InsertSubtreeAsync(subtree, parentNodeKey, cancellationToken);

    /// <summary>Imports multiple independently identified trees under one atomic transaction boundary.</summary>
    /// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
    /// <param name="trees">The detached root branches and their never-before-used TreeIds.</param>
    /// <param name="cancellationToken">The token used for validation, ordered locking, and bounded writes.</param>
    /// <returns>A task that completes when every tree is visible or none of them is written.</returns>
    public Task InsertForestAsync<TTreeId>(
        IReadOnlyList<NestedSetTreeImport<TEntity, TTreeId>> trees,
        CancellationToken cancellationToken = default
    )
        where TTreeId : notnull => _mutations.InsertForestAsync(trees, cancellationToken);

    /// <summary>Inserts a detached node as the first child when manual placement is enabled.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="entity">The detached node to insert.</param>
    /// <param name="parentNodeKey">The parent whose Scope and TreeId are resolved in the database.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the atomic mutation boundary.</returns>
    /// <exception cref="NestedSetException">The hierarchy uses configured-only ordering.</exception>
    public Task InsertAsFirstChildAsync<TKey>(
        TEntity entity,
        TKey parentNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.InsertChildAsync(
        entity,
        parentNodeKey,
        NestedSetPlacement.FirstChild,
        automatic: false,
        cancellationToken);

    /// <summary>Inserts a detached node as the last child when manual placement is enabled.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="entity">The detached node to insert.</param>
    /// <param name="parentNodeKey">The parent whose Scope and TreeId are resolved in the database.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the atomic mutation boundary.</returns>
    /// <exception cref="NestedSetException">The hierarchy uses configured-only ordering.</exception>
    public Task InsertAsLastChildAsync<TKey>(
        TEntity entity,
        TKey parentNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.InsertChildAsync(
        entity,
        parentNodeKey,
        NestedSetPlacement.LastChild,
        automatic: false,
        cancellationToken);

    /// <summary>Inserts a detached node immediately before a sibling when manual placement is enabled.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="entity">The detached node to insert.</param>
    /// <param name="siblingNodeKey">The sibling whose parent, Scope, and TreeId determine placement.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the atomic mutation boundary.</returns>
    /// <exception cref="NestedSetException">The hierarchy uses configured-only ordering.</exception>
    public Task InsertBeforeAsync<TKey>(
        TEntity entity,
        TKey siblingNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.InsertChildAsync(
        entity,
        siblingNodeKey,
        NestedSetPlacement.Before,
        automatic: false,
        cancellationToken);

    /// <summary>Inserts a detached node immediately after a sibling when manual placement is enabled.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="entity">The detached node to insert.</param>
    /// <param name="siblingNodeKey">The sibling whose parent, Scope, and TreeId determine placement.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the atomic mutation boundary.</returns>
    /// <exception cref="NestedSetException">The hierarchy uses configured-only ordering.</exception>
    public Task InsertAfterAsync<TKey>(
        TEntity entity,
        TKey siblingNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.InsertChildAsync(
        entity,
        siblingNodeKey,
        NestedSetPlacement.After,
        automatic: false,
        cancellationToken);

    /// <summary>Moves a subtree under a parent, including across trees, using configured sibling ordering.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="nodeKey">The root of the subtree to move.</param>
    /// <param name="parentNodeKey">The new parent in the same Scope.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after both affected trees are atomically updated.</returns>
    public Task MoveToAsync<TKey>(
        TKey nodeKey,
        TKey parentNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.MoveAsync(
        nodeKey,
        parentNodeKey,
        NestedSetPlacement.LastChild,
        automatic: true,
        cancellationToken);

    /// <summary>Moves a subtree immediately before a same-tree sibling when manual placement is enabled.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="nodeKey">The root of the subtree to move.</param>
    /// <param name="siblingNodeKey">The same-tree sibling that follows the moved subtree.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the atomic mutation boundary.</returns>
    /// <exception cref="NestedSetException">The hierarchy uses configured-only ordering.</exception>
    public Task MoveBeforeAsync<TKey>(
        TKey nodeKey,
        TKey siblingNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.MoveAsync(
        nodeKey,
        siblingNodeKey,
        NestedSetPlacement.Before,
        automatic: false,
        cancellationToken);

    /// <summary>Moves a subtree immediately after a same-tree sibling when manual placement is enabled.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="nodeKey">The root of the subtree to move.</param>
    /// <param name="siblingNodeKey">The same-tree sibling that precedes the moved subtree.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the atomic mutation boundary.</returns>
    /// <exception cref="NestedSetException">The hierarchy uses configured-only ordering.</exception>
    public Task MoveAfterAsync<TKey>(
        TKey nodeKey,
        TKey siblingNodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.MoveAsync(
        nodeKey,
        siblingNodeKey,
        NestedSetPlacement.After,
        automatic: false,
        cancellationToken);

    /// <summary>Detaches a subtree as a new root under a never-before-used TreeId.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
    /// <param name="nodeKey">The root of the subtree to detach.</param>
    /// <param name="newTreeId">The never-before-used identity reserved for the detached tree.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the source and new tree are atomically updated.</returns>
    public Task DetachAsTreeAsync<TKey, TTreeId>(
        TKey nodeKey,
        TTreeId newTreeId,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
        where TTreeId : notnull => _mutations.DetachAsTreeAsync(nodeKey, newTreeId, cancellationToken);

    /// <summary>Deletes one non-root node and promotes its direct children.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="nodeKey">The non-root node to delete.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the node is deleted and its children are promoted.</returns>
    public Task DeleteAsync<TKey>(
        TKey nodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.DeleteAsync(nodeKey, subtree: false, cancellationToken);

    /// <summary>Deletes one node and all descendants, retiring the TreeId when the node is the root.</summary>
    /// <typeparam name="TKey">The exact mapped NodeKey type.</typeparam>
    /// <param name="nodeKey">The root of the subtree to delete.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the subtree deletion and any registry retirement.</returns>
    public Task DeleteSubtreeAsync<TKey>(
        TKey nodeKey,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull => _mutations.DeleteAsync(nodeKey, subtree: true, cancellationToken);

    /// <summary>Deletes an entire explicit tree and permanently retires its TreeId.</summary>
    /// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
    /// <param name="treeId">The identity of the tree to delete.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the tree is deleted and its identity is retired.</returns>
    public Task DeleteTreeAsync<TTreeId>(
        TTreeId treeId,
        CancellationToken cancellationToken = default
    )
        where TTreeId : notnull => _mutations.DeleteTreeAsync(treeId, cancellationToken);

    /// <summary>Administratively removes an empty tombstone so its TreeId may be reused deliberately.</summary>
    /// <typeparam name="TTreeId">The exact mapped TreeId type.</typeparam>
    /// <param name="treeId">The tombstoned identity to purge.</param>
    /// <param name="cancellationToken">The token used for locking and database work.</param>
    /// <returns>A task that completes after the tombstone is removed atomically.</returns>
    /// <exception cref="NestedSetException">
    /// The identity is missing, remains active, or still has hierarchy nodes.
    /// </exception>
    /// <remarks>
    /// This administrative operation permits later reuse of the same TreeId. Applications should retain their
    /// audit evidence before purging and authorize this operation separately from ordinary hierarchy writes.
    /// </remarks>
    public Task PurgeTreeIdAsync<TTreeId>(
        TTreeId treeId,
        CancellationToken cancellationToken = default
    )
        where TTreeId : notnull => _mutations.PurgeTreeIdAsync(treeId, cancellationToken);
}
