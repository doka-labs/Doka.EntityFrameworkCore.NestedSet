namespace Doka.EntityFrameworkCore.NestedSet.Storage;

internal sealed partial class NestedSetStore<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Gets the structural-only projection; entity constructors and payload columns are excluded.</summary>
    internal IQueryable<NestedSetNode<TKey>> Structure => Nodes.Select(Map.Projection);

    /// <summary>Closes the parent join over its actual nullable CLR type once per typed store.</summary>
    private static readonly
        Func<IQueryable<TEntity>, IProperty, IProperty, string?, IQueryable<NestedSetParentLink<TKey>>> s_parentLinks =
            typeof(NestedSetStore<TEntity, TKey, TTreeId, TScope>).GetMethod(
                    nameof(ParentLinksTyped),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                // WHY: EF can require a nullable CLR key; its parent reuses that nullable type instead of nesting it.
                .MakeGenericMethod(
                    typeof(TKey).IsValueType && Nullable.GetUnderlyingType(typeof(TKey)) is null
                        ? typeof(Nullable<>).MakeGenericType(typeof(TKey))
                        : typeof(TKey))
                .CreateDelegate<Func<IQueryable<TEntity>, IProperty, IProperty, string?,
                    IQueryable<NestedSetParentLink<TKey>>>>();

    /// <summary>Gets canonical parent links according to the database's principal-key comparison rules.</summary>
    internal IQueryable<NestedSetParentLink<TKey>> ParentLinks =>
        s_parentLinks(Nodes, Map.KeyProperty, Map.ParentProperty, Map.KeyCollation);

    /// <summary>Joins typed nullable parent storage to typed principal keys without materializing payload.</summary>
    private static IQueryable<NestedSetParentLink<TKey>> ParentLinksTyped<TParent>(
        IQueryable<TEntity> nodes,
        IProperty key,
        IProperty parent,
        string? collation
    )
    {
        var principal = Expression.Parameter(typeof(TEntity), "principal");
        var child = Expression.Parameter(typeof(TEntity), "child");
        var principalKey = Expression.Convert(NestedSetExpressions.Property(principal, key), typeof(TParent));
        Expression parentKey = NestedSetExpressions.Property(child, parent);

        if (collation is not null)
        {
            // WHY: The nullable parent column must use the referenced principal's collation when aliases differ.
            // Collate preserves the actual mapped nullable type and its provider converter inside the SQL join.
            parentKey = Expression.Call(
                typeof(RelationalDbFunctionsExtensions),
                nameof(RelationalDbFunctionsExtensions.Collate),
                [typeof(TParent)],
                Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                parentKey,
                Expression.Constant(collation));
        }

        var principalSelector = Expression.Lambda<Func<TEntity, TParent>>(principalKey, principal);
        var parentSelector = Expression.Lambda<Func<TEntity, TParent>>(parentKey, child);
        var pairs = nodes.Join(
            nodes,
            principalSelector,
            parentSelector,
            (
                principalNode,
                childNode
            ) => new
            {
                Parent = principalNode,
                Child = childNode,
            });

        return pairs.Select(pair => new NestedSetParentLink<TKey>(
            EF.Property<TKey>(pair.Child, key.Name),
            EF.Property<TKey>(pair.Parent, key.Name)));
    }

    /// <summary>Gets all structural nodes in this exact tree without tracking.</summary>
    /// <value>A deferred query over every node in this exact tree.</value>
    /// <remarks>
    ///     Scope equality follows the database's configured comparison rules. Untracked results cannot become stale
    ///     change-tracker entries when subsequent operations update the same rows directly in SQL.
    /// </remarks>
    internal IQueryable<TEntity> Nodes => _nodes ??= CreateNodes();

    /// <summary>Builds the deferred exact-tree query once from this store's immutable identity snapshots.</summary>
    private IQueryable<TEntity> CreateNodes()
    {
        // WHY: A context-scoped store never changes its selected Scope or TreeId. Reusing this expression avoids
        // rebuilding both predicates for every structural read; query execution still reads current database rows.
        var nodes = Set
            .IgnoreQueryFilters()
            .AsNoTracking();

        if (Map.Scope is { } scope)
        {
            nodes = nodes.Where(Equal(scope, _scope));
        }

        return nodes.Where(TreeIdentity());
    }

    /// <summary>Builds the exact typed tree predicate without boxing identity parameters.</summary>
    private Expression<Func<TEntity, bool>> TreeIdentity()
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var property = NestedSetExpressions.Property(parameter, Map.TreeId, typeof(TTreeId));

        // WHY: A typed captured value gives EF a stable parameter type for binary or converted identities,
        // and avoids erasing a known value type solely to reconstruct its type in the expression.
        return Expression.Lambda<Func<TEntity, bool>>(
            NestedSetTypedValue<TTreeId>.Equal(property, Capture(_treeId)),
            parameter);
    }

    /// <summary>Builds a scoped query for one direct sibling group.</summary>
    /// <param name="parent">The typed parent identity, or an absent value for the tree root.</param>
    /// <returns>A query suitable for both reads and set-based updates.</returns>
    /// <remarks>
    ///     A direct parent predicate avoids updating a table through a self-referencing subquery, which MySQL rejects.
    /// </remarks>
    internal IQueryable<TEntity> Siblings(
        NestedSetParent<TKey> parent
    ) => Nodes.Where(ParentEqual(parent));

    /// <summary>Builds native equality while retaining the known mapped role type.</summary>
    /// <typeparam name="TValue">The mapped key, scope or tree identity type.</typeparam>
    /// <param name="name">The mapped property name.</param>
    /// <param name="value">The typed comparison value.</param>
    /// <returns>A parameterized predicate without erasing a known scalar type.</returns>
    internal Expression<Func<TEntity, bool>> Equal<TValue>(
        string name,
        TValue value
    )
        where TValue : notnull
    {
        var property = Map.Property<TValue>(name);

        return Expression.Lambda<Func<TEntity, bool>>(
            NestedSetTypedValue<TValue>.Equal(property.Body, Capture(value)),
            property.Parameters);
    }

    /// <summary>Matches one typed parent group using actual nullable storage and principal-key collation.</summary>
    /// <param name="parent">The typed parent identity, or an absent value for the tree root.</param>
    /// <returns>A direct predicate suitable for reads and MySQL-safe set-based updates.</returns>
    internal Expression<Func<TEntity, bool>> ParentEqual(
        NestedSetParent<TKey> parent
    )
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var access = NestedSetExpressions.Property(parameter, Map.ParentProperty);
        var present = NestedSetParent<TKey>.Presence(access);

        if (!parent.HasValue)
        {
            return Expression.Lambda<Func<TEntity, bool>>(Expression.Not(present), parameter);
        }

        Expression value = access.Type == typeof(TKey) ? access : Expression.Convert(access, typeof(TKey));

        if (Map.KeyCollation is { } collation)
        {
            // WHY: A parent alias must identify its principal row even when the parent column has another collation.
            value = Expression.Call(
                typeof(RelationalDbFunctionsExtensions),
                nameof(RelationalDbFunctionsExtensions.Collate),
                [typeof(TKey)],
                Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                value,
                Expression.Constant(collation));
        }

        return Expression.Lambda<Func<TEntity, bool>>(
            Expression.AndAlso(present, NestedSetTypedValue<TKey>.Equal(value, Capture(parent.Value))),
            parameter);
    }

    /// <summary>Selects non-root rows through the mapped parent's real nullable CLR type.</summary>
    internal Expression<Func<TEntity, bool>> HasParent()
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var access = NestedSetExpressions.Property(parameter, Map.ParentProperty);

        return Expression.Lambda<Func<TEntity, bool>>(NestedSetParent<TKey>.Presence(access), parameter);
    }

    /// <summary>Creates a closure member so EF extracts a SQL parameter instead of embedding a runtime
    /// constant.</summary>
    /// <typeparam name="TValue">The value's CLR type.</typeparam>
    /// <param name="value">The parameter value.</param>
    /// <returns>An expression suitable for EF parameter extraction.</returns>
    internal static Expression Capture<TValue>(
        TValue value
    )
    {
        Expression<Func<TValue>> capture = () => value;
        return capture.Body;
    }

    /// <summary>Returns the cached selector for an exact mapped property type.</summary>
    /// <typeparam name="TValue">The property's CLR type.</typeparam>
    /// <param name="name">The mapped property name.</param>
    /// <returns>An immutable selector without captured operation values.</returns>
    internal Expression<Func<TEntity, TValue>> Property<TValue>(
        string name
    ) => Map.Property<TValue>(name);

    /// <summary>Loads only the structural snapshot of a required node in this scope.</summary>
    /// <param name="key">The scoped primary key.</param>
    /// <param name="cancellationToken">The database cancellation token.</param>
    /// <returns>The persisted structural snapshot.</returns>
    /// <exception cref="InvalidOperationException">The key is missing from this scope.</exception>
    internal async Task<NestedSetNode<TKey>> FindAsync(
        TKey key,
        CancellationToken cancellationToken
    )
    {
        var node = await Nodes
            .Where(Equal(Map.Key, key))
            .Select(Map.Projection)
            .Select(value => (NestedSetNode<TKey>?)value)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return node
            ?? throw new NestedSetException(NestedSetErrorCode.NodeNotFound, "The node does not exist in this scope.");
    }

    /// <summary>Reads the tree's highest right boundary, including the empty-tree case.</summary>
    /// <param name="cancellationToken">The token used to cancel the database read.</param>
    /// <returns>The maximum boundary, or zero for an empty tree.</returns>
    internal async Task<long> MaximumAsync(
        CancellationToken cancellationToken
    ) => await Nodes
            .Select(node => (long?)EF.Property<long>(node, Map.Right))
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false)
        ?? 0;
}
