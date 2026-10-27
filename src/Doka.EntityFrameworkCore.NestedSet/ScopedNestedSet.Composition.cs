namespace Doka.EntityFrameworkCore.NestedSet;

public sealed partial class ScopedNestedSet<TEntity, TScope>
    where TEntity : class
    where TScope : notnull
{
    /// <summary>Projects each selected node with every visible strict ancestor and structural distance.</summary>
    /// <typeparam name="TResult">The application-owned projection type.</typeparam>
    /// <param name="nodes">A predicate selecting source nodes from this bound hierarchy.</param>
    /// <param name="projection">The projection whose parameters are source node, ancestor, and distance.</param>
    /// <returns>An unordered deferred query containing one row per visible source-ancestor pair.</returns>
    /// <remarks>
    ///     Distance is the mapped source depth minus ancestor depth and is at least one.
    ///     Hidden intermediate nodes do not compress distance or imply a complete visible path.
    ///     Query construction performs no database I/O; execute with EF async operators.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required query or lambda argument is null.</exception>
    public IQueryable<TResult> SelectAncestors<TResult>(
        Expression<Func<TEntity, bool>> nodes,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    ) => _query.SelectAncestors(nodes, projection);

    /// <summary>Projects each selected node with itself and every visible ancestor with structural distance.</summary>
    /// <typeparam name="TResult">The application-owned projection type.</typeparam>
    /// <param name="nodes">A predicate selecting source nodes from this bound hierarchy.</param>
    /// <param name="projection">The projection whose parameters are source node, ancestor, and distance.</param>
    /// <returns>An unordered deferred query containing one row per visible source-ancestor-or-self pair.</returns>
    /// <remarks>
    ///     Self has distance zero. Other distances use configured depth metadata.
    ///     All pairs retain the exact mapping, scope, and tree identity of their source.
    ///     This method applies no implicit rank, ordering, business edge condition, or complete-path filter.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required query or lambda argument is null.</exception>
    public IQueryable<TResult> SelectAncestorsOrSelf<TResult>(
        Expression<Func<TEntity, bool>> nodes,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    ) => _query.SelectAncestorsOrSelf(nodes, projection);

    /// <summary>Projects each selected anchor and each visible node in its inclusive subtree.</summary>
    /// <typeparam name="TResult">The application-owned projection type.</typeparam>
    /// <param name="anchors">A predicate selecting anchors from this bound hierarchy.</param>
    /// <param name="projection">The projection whose parameters are anchor, node, and structural distance.</param>
    /// <returns>An unordered, deferred query preserving one row per anchor-node pair.</returns>
    /// <remarks>
    ///     Visible endpoints suffice; hidden intermediate nodes do not remove pairs or compress distance.
    ///     Use WherePathEdges when the application requires a complete visible allowed path.
    ///     Query construction performs no database I/O; use EF async operators for execution.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required query or lambda argument is null.</exception>
    public IQueryable<TResult> SelectSubtrees<TResult>(
        Expression<Func<TEntity, bool>> anchors,
        Expression<Func<TEntity, TEntity, int, TResult>> projection
    ) => _query.SelectSubtrees(anchors, projection);

    /// <summary>Keeps rows whose complete visible anchor-to-node path has only allowed direct edges.</summary>
    /// <typeparam name="TRow">The application-owned source projection type.</typeparam>
    /// <param name="source">A query from the same context carrying endpoints of this exact hierarchy mapping.</param>
    /// <param name="anchor">The mapped anchor entity carried by each source row.</param>
    /// <param name="node">The mapped destination entity carried by each source row.</param>
    /// <param name="edgeAllowed">The condition whose parameters are row, direct parent, and child.</param>
    /// <returns>The source rows with a complete allowed path; no additional multiplicity or ordering.</returns>
    /// <remarks>
    ///     The upper anchor is exclusive and the lower node is inclusive for direct-edge checks.
    ///     A hidden endpoint or intermediate node rejects the row; an empty self path still requires visibility.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required query or lambda argument is null.</exception>
    /// <exception cref="ArgumentException">
    ///     The source uses another provider or a selected endpoint is not an alias of this hierarchy.
    /// </exception>
    public IQueryable<TRow> WherePathEdges<TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> anchor,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, TEntity, bool>> edgeAllowed
    ) => _query.WherePathEdges(source, anchor, node, edgeAllowed);

    /// <summary>Keeps rows with one visible ancestor or self satisfying the complete condition.</summary>
    /// <typeparam name="TRow">The application-owned source projection type.</typeparam>
    /// <param name="source">A query from the same context carrying endpoints of this exact hierarchy mapping.</param>
    /// <param name="node">The mapped entity whose ancestors and self are examined.</param>
    /// <param name="predicate">The condition whose parameters are row and candidate owner.</param>
    /// <returns>The source rows with at least one matching owner, without multiplying rows.</returns>
    /// <remarks>
    ///     A visible matching owner may qualify across hidden intermediate nodes.
    ///     This existential check does not require the complete-path visibility used by Nearest.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required query or lambda argument is null.</exception>
    /// <exception cref="ArgumentException">
    ///     The source uses another provider or a selected endpoint is not an alias of this hierarchy.
    /// </exception>
    public IQueryable<TRow> WhereAnyAncestorOrSelf<TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, bool>> predicate
    ) => _query.WhereAnyAncestorOrSelf(source, node, predicate);

    /// <summary>Keeps rows whose nearest candidate ancestor or self satisfies the complete condition.</summary>
    /// <typeparam name="TRow">The application-owned source projection type.</typeparam>
    /// <param name="source">A query from the same context carrying endpoints of this exact hierarchy mapping.</param>
    /// <param name="node">The mapped entity whose ancestors and self are examined.</param>
    /// <param name="candidate">The owner-selection condition over row and owner, independent of predicate.</param>
    /// <param name="predicate">The condition over row and owner required of the selected nearest candidate.</param>
    /// <returns>Source rows whose nearest candidate satisfies the predicate and has a complete visible path.</returns>
    /// <remarks>
    ///     A failing predicate never falls back to a more distant candidate. Self is eligible.
    ///     A hidden intervening node rejects the row. Business edge validity remains a separate path filter.
    ///     Query construction performs no database I/O; execute the returned query with EF async operators.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required query or lambda argument is null.</exception>
    /// <exception cref="ArgumentException">
    ///     The source uses another provider or a selected endpoint is not an alias of this hierarchy.
    /// </exception>
    public IQueryable<TRow> WhereNearestAncestorOrSelf<TRow>(
        IQueryable<TRow> source,
        Expression<Func<TRow, TEntity>> node,
        Expression<Func<TRow, TEntity, bool>> candidate,
        Expression<Func<TRow, TEntity, bool>> predicate
    ) => _query.WhereNearestAncestorOrSelf(source, node, candidate, predicate);
}
