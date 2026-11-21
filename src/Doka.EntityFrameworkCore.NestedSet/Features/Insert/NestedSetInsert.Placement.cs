namespace Doka.EntityFrameworkCore.NestedSet.Features.Insert;

internal sealed partial class NestedSetInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Creates the sole root of the store's new tree.</summary>
    /// <param name="entity">The detached root whose managed structure is assigned during insertion.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertRootAsync(
        TEntity entity,
        CancellationToken cancellationToken
    ) => InsertAsync(entity, default, null, automatic: true, cancellationToken);

    /// <summary>Inserts a child using the configured automatic rule or the last sibling position.</summary>
    /// <param name="entity">The detached child whose managed structure is assigned during insertion.</param>
    /// <param name="parent">The existing parent key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertChildAsync(
        TEntity entity,
        TKey parent,
        CancellationToken cancellationToken
    ) => InsertAsync(entity, parent, NestedSetPlacement.LastChild, automatic: true, cancellationToken);

    /// <summary>Inserts a child at the first sibling position when manual placement is permitted.</summary>
    /// <param name="entity">The detached child to insert.</param>
    /// <param name="parent">The existing parent key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertAsFirstChildAsync(
        TEntity entity,
        TKey parent,
        CancellationToken cancellationToken
    ) => InsertAsync(entity, parent, NestedSetPlacement.FirstChild, automatic: false, cancellationToken);

    /// <summary>Inserts a child at the last sibling position when manual placement is permitted.</summary>
    /// <param name="entity">The detached child to insert.</param>
    /// <param name="parent">The existing parent key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertAsLastChildAsync(
        TEntity entity,
        TKey parent,
        CancellationToken cancellationToken
    ) => InsertAsync(entity, parent, NestedSetPlacement.LastChild, automatic: false, cancellationToken);

    /// <summary>Inserts a sibling immediately before an existing non-root node.</summary>
    /// <param name="entity">The detached sibling to insert.</param>
    /// <param name="sibling">The existing sibling key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertBeforeAsync(
        TEntity entity,
        TKey sibling,
        CancellationToken cancellationToken
    ) => InsertAsync(entity, sibling, NestedSetPlacement.Before, automatic: false, cancellationToken);

    /// <summary>Inserts a sibling immediately after an existing non-root node.</summary>
    /// <param name="entity">The detached sibling to insert.</param>
    /// <param name="sibling">The existing sibling key within the store's tree.</param>
    /// <param name="cancellationToken">The token used for lock acquisition and database work.</param>
    /// <returns>A task that completes after the owned commit or caller savepoint release.</returns>
    internal Task InsertAfterAsync(
        TEntity entity,
        TKey sibling,
        CancellationToken cancellationToken
    ) => InsertAsync(entity, sibling, NestedSetPlacement.After, automatic: false, cancellationToken);
}
