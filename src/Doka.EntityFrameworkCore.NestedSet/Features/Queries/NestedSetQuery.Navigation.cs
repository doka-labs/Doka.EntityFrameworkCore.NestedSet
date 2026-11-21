namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

internal sealed partial class NestedSetQuery<TEntity>
    where TEntity : class
{
    /// <summary>Returns an anchor and all visible descendants in preorder.</summary>
    /// <typeparam name="TKey">The mapped non-null NodeKey type inferred from the argument.</typeparam>
    /// <param name="nodeKey">The visible subtree anchor.</param>
    /// <returns>A composable no-tracking query; empty when the anchor is absent or filtered.</returns>
    internal IQueryable<TEntity> SubtreeOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull
    {
        var anchor = Anchor(nodeKey);
        var treeId = _descriptor.TreeId.Name;
        var left = _descriptor.Left.Name;
        var right = _descriptor.Right.Name;
        var nodes = _nodes;

        // WHY: An anchor-driven join exposes TreeId and boundary ranges to the index planner instead of
        // testing a correlated EXISTS for every candidate row in the application scope.
        return anchor
            .SelectMany(value => nodes.Where(node =>
                EF.Property<object>(node, treeId) == EF.Property<object>(value, treeId)
                && EF.Property<long>(node, left) >= EF.Property<long>(value, left)
                && EF.Property<long>(node, left) <= EF.Property<long>(value, right)
                && EF.Property<long>(node, right) <= EF.Property<long>(value, right)))
            .OrderBy(Left());
    }

    /// <summary>Returns the visible direct children of an anchor in sibling order.</summary>
    /// <typeparam name="TKey">The mapped non-null NodeKey type inferred from the argument.</typeparam>
    /// <param name="nodeKey">The visible parent key.</param>
    /// <returns>A composable no-tracking query; empty when the parent is absent or filtered.</returns>
    internal IQueryable<TEntity> ChildrenOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull
    {
        var anchor = Anchor(nodeKey);
        var key = _descriptor.NodeKey.Name;
        var treeId = _descriptor.TreeId.Name;

        // WHY: The query root does not know the TreeId or nullable Parent CLR type. These object selectors are
        // translation-only mapped column expressions, preserving converters and native equality without
        // transporting runtime identities as object or materializing either endpoint.
        var pairs = anchor.Join(
            _nodes,
            parent => EF.Property<object>(parent, key),
            ParentKey(),
            (parent,
                child) => new
            {
                Parent = parent,
                Child = child,
            });

        // WHY: ParentId selects one sibling group through its configured index, independent of subtree size.
        // Exact TreeId equality also prevents an invalid cross-tree adjacency from leaking another tree's rows.
        return pairs
            .Where(pair => EF.Property<object>(pair.Parent, treeId) == EF.Property<object>(pair.Child, treeId))
            .Select(pair => pair.Child)
            .OrderBy(Position());
    }

    /// <summary>Returns all visible strict descendants of an anchor in preorder.</summary>
    /// <typeparam name="TKey">The mapped non-null NodeKey type inferred from the argument.</typeparam>
    /// <param name="nodeKey">The visible ancestor key.</param>
    /// <returns>A composable no-tracking query; empty when the ancestor is absent or filtered.</returns>
    internal IQueryable<TEntity> DescendantsOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull
    {
        var anchor = Anchor(nodeKey);
        var treeId = _descriptor.TreeId.Name;
        var left = _descriptor.Left.Name;
        var right = _descriptor.Right.Name;
        var nodes = _nodes;

        // WHY: An anchor-driven join exposes TreeId and boundary ranges to the index planner instead of
        // testing a correlated EXISTS for every candidate row in the application scope.
        return anchor
            .SelectMany(value => nodes.Where(node =>
                EF.Property<object>(node, treeId) == EF.Property<object>(value, treeId)
                && EF.Property<long>(node, left) > EF.Property<long>(value, left)
                && EF.Property<long>(node, left) < EF.Property<long>(value, right)
                && EF.Property<long>(node, right) < EF.Property<long>(value, right)))
            .OrderBy(Left());
    }

    /// <summary>Returns all visible strict ancestors from root to direct parent.</summary>
    /// <typeparam name="TKey">The mapped non-null NodeKey type inferred from the argument.</typeparam>
    /// <param name="nodeKey">The visible descendant key.</param>
    /// <returns>A composable no-tracking query; empty when the descendant is absent or filtered.</returns>
    internal IQueryable<TEntity> AncestorsOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull
    {
        var anchor = Anchor(nodeKey);
        var treeId = _descriptor.TreeId.Name;
        var left = _descriptor.Left.Name;
        var right = _descriptor.Right.Name;
        var nodes = _nodes;

        // WHY: An anchor-driven join exposes TreeId and boundary ranges to the index planner instead of
        // testing a correlated EXISTS for every candidate row in the application scope.
        return anchor
            .SelectMany(value => nodes.Where(node =>
                EF.Property<object>(node, treeId) == EF.Property<object>(value, treeId)
                && EF.Property<long>(node, left) < EF.Property<long>(value, left)
                && EF.Property<long>(node, right) > EF.Property<long>(value, right)))
            .OrderBy(Left());
    }

    /// <summary>Returns the visible direct parent of an anchor.</summary>
    /// <typeparam name="TKey">The mapped non-null NodeKey type inferred from the argument.</typeparam>
    /// <param name="nodeKey">The visible child key.</param>
    /// <returns>A composable no-tracking query containing at most one row.</returns>
    internal IQueryable<TEntity> ParentOf<TKey>(
        TKey nodeKey
    )
        where TKey : notnull
    {
        var anchor = Anchor(nodeKey);
        var key = _descriptor.NodeKey.Name;
        var treeId = _descriptor.TreeId.Name;
        var pairs = anchor.Join(
            _nodes,
            ParentKey(),
            parent => EF.Property<object>(parent, key),
            (child, parent) => new
            {
                Child = child,
                Parent = parent,
            });

        // WHY: The stored parent key permits a unique lookup rather than scanning every ancestor interval.
        return pairs
            .Where(pair => EF.Property<object>(pair.Child, treeId) == EF.Property<object>(pair.Parent, treeId))
            .Select(pair => pair.Parent);
    }

    /// <summary>Reads parent references using the principal node key's database comparison rules.</summary>
    private Expression<Func<TEntity, object>> ParentKey()
    {
        var node = Expression.Parameter(typeof(TEntity), "node");
        Expression parent = NestedSetExpressions.Property(node, _descriptor.Parent.Resolve(_entityType));

        if (_keyCollation is { } collation)
        {
            // WHY: Parent storage may be text behind a reference or nullable value converter. Retaining its
            // mapped model type preserves that converter while the principal's collation defines identity.
            parent = Expression.Call(
                typeof(RelationalDbFunctionsExtensions),
                nameof(RelationalDbFunctionsExtensions.Collate),
                [parent.Type],
                Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                parent,
                Expression.Constant(collation));
        }

        return Expression.Lambda<Func<TEntity, object>>(Expression.Convert(parent, typeof(object)), node);
    }
}
