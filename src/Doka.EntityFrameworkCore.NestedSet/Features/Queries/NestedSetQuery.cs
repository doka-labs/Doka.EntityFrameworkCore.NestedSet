namespace Doka.EntityFrameworkCore.NestedSet.Features.Queries;

/// <summary>Builds public hierarchy queries from finalized metadata without materializing anchor entities.</summary>
/// <typeparam name="TEntity">The configured hierarchy entity.</typeparam>
internal sealed partial class NestedSetQuery<TEntity>
    where TEntity : class
{
    private readonly IQueryable<TEntity> _nodes;
    private readonly IQueryable<TEntity> _unboundNodes;

    /// <summary>Owns cumulative identity constraints, or null before the first scope/tree binding.</summary>
    private readonly Expression<Func<TEntity, bool>>? _binding;
    private readonly DbContext _context;
    private readonly IEntityType _entityType;
    private readonly NestedSetModelDescriptor _descriptor;
    private readonly bool _scopeBound;
    private readonly bool _treeBound;
    private readonly NestedSetProviderCapabilities _capabilities;
    private readonly string? _keyCollation;

    /// <summary>Creates an unbound public query root and validates the hierarchy configuration immediately.</summary>
    /// <param name="context">The caller-owned context.</param>
    /// <param name="entityType">The exact ordinary or named shared hierarchy entity type.</param>
    internal NestedSetQuery(
        DbContext context,
        IEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entityType);

        _context = context;
        _entityType = entityType;
        _descriptor = NestedSetModelMapping
            .For(context.Model)
            .Descriptor(_entityType);

        _nodes = NestedSetEntityAccess<TEntity>
            .Set(context, _entityType)
            .AsNoTracking();

        _unboundNodes = _nodes;
        _scopeBound = _descriptor.Scope is null;
        _capabilities = NestedSetProviderCapabilities.Resolve(context);
        _keyCollation = NestedSetCollations.Resolve(context, _descriptor.NodeKey.Resolve(_entityType), _entityType);
    }

    /// <summary>Creates a new immutable binding over an already validated public query root.</summary>
    private NestedSetQuery(
        NestedSetQuery<TEntity> source,
        Expression<Func<TEntity, bool>> binding,
        bool scopeBound,
        bool treeBound
    )
    {
        _binding = binding;
        _unboundNodes = source._unboundNodes;
        _nodes = _unboundNodes.Where(binding);
        _context = source._context;
        _entityType = source._entityType;
        _descriptor = source._descriptor;
        _scopeBound = scopeBound;
        _treeBound = treeBound;
        _capabilities = source._capabilities;
        _keyCollation = source._keyCollation;
    }

    /// <summary>Restores only EF-normalized roots for one deferred compilation without rebinding caller identities.</summary>
    internal NestedSetQuery(
        DbContext context,
        IEntityType entityType,
        IQueryable<TEntity> visible,
        IQueryable<TEntity> unbound,
        bool treeBound
    ) : this(context, entityType)
    {
        // WHY: The original public call already checked Scope. Its exact normalized visible root owns
        // all Scope/Tree predicates; reconstructing scalar bindings would freeze different captured values.
        _nodes = visible;
        _unboundNodes = unbound;
        _scopeBound = true;
        _treeBound = treeBound;
    }

    /// <summary>Binds the configured application partition using the exact mapped Scope type.</summary>
    /// <typeparam name="TScope">The mapped non-null Scope type inferred from the argument.</typeparam>
    /// <param name="scope">The application partition to bind.</param>
    /// <returns>A new immutable query binding.</returns>
    internal NestedSetQuery<TEntity> ForScope<TScope>(
        TScope scope
    )
        where TScope : notnull
    {
        var descriptor = _descriptor.Scope
            ?? throw new InvalidOperationException(
                $"Hierarchy '{_entityType.Name}' has no Scope. Do not call ForScope().");

        RequireType<TScope>(descriptor, NestedSetPropertyRoles.Scope);
        var property = descriptor.Resolve(_entityType);
        ArgumentNullException.ThrowIfNull(scope);
        var snapshot = NestedSetTypedValue<TScope>.Snapshot(property, scope);

        return new NestedSetQuery<TEntity>(
            this,
            Bind(property, snapshot),
            scopeBound: true,
            treeBound: _treeBound);
    }

    /// <summary>Returns one complete tree in stable nested-set preorder.</summary>
    /// <typeparam name="TTreeId">The mapped non-null TreeId type inferred from the argument.</typeparam>
    /// <param name="treeId">The stable tree identity.</param>
    /// <param name="operations">The exact validated hierarchy operations sharing the owned identity.</param>
    /// <returns>A tree facade whose query and maintenance operations share one immutable snapshot.</returns>
    internal NestedSetTree<TEntity, TTreeId> InTree<TTreeId>(
        TTreeId treeId,
        NestedSetMutationBinding<TEntity> operations
    )
        where TTreeId : notnull
    {
        RequireScope();
        RequireType<TTreeId>(_descriptor.TreeId, NestedSetPropertyRoles.TreeId);

        var property = _descriptor.TreeId.Resolve(_entityType);
        ArgumentNullException.ThrowIfNull(treeId);
        var snapshot = NestedSetTypedValue<TTreeId>.Snapshot(property, treeId);

        // WHY: Query execution is deferred. Its predicate and later tree maintenance must own the same identity
        // rather than independently snapshotting a caller value that can change between those bindings.
        return new NestedSetTree<TEntity, TTreeId>(
            new NestedSetQuery<TEntity>(
                this,
                Bind(property, snapshot),
                scopeBound: _scopeBound,
                treeBound: true),
            operations,
            snapshot);
    }

    /// <summary>Returns the complete tree containing an existing visible anchor.</summary>
    /// <typeparam name="TKey">The mapped non-null NodeKey type inferred from the argument.</typeparam>
    /// <param name="nodeKey">The visible anchor key.</param>
    /// <returns>A composable no-tracking query in preorder; empty when the anchor is absent or filtered.</returns>
    internal IQueryable<TEntity> TreeContaining<TKey>(
        TKey nodeKey
    )
        where TKey : notnull
    {
        var anchor = Anchor(nodeKey);
        var treeId = _descriptor.TreeId.Name;
        var nodes = _nodes;

        // WHY: An anchor-driven join exposes TreeId and boundary ranges to the index planner instead of
        // testing a correlated EXISTS for every candidate row in the application scope.
        return anchor
            .SelectMany(value => nodes.Where(node =>
                EF.Property<object>(node, treeId) == EF.Property<object>(value, treeId)))
            .OrderBy(Left());
    }

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
            (
                parent,
                child
            ) => new
            {
                Parent = parent,
                Child = child
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

    /// <summary>Selects one visible anchor by exact mapped NodeKey type without executing the query.</summary>
    private IQueryable<TEntity> Anchor<TKey>(
        TKey nodeKey
    )
        where TKey : notnull
    {
        RequireScope();
        RequireType<TKey>(_descriptor.NodeKey, NestedSetPropertyRoles.NodeKey);
        var property = _descriptor.NodeKey.Resolve(_entityType);
        ArgumentNullException.ThrowIfNull(nodeKey);
        var snapshot = NestedSetTypedValue<TKey>.Snapshot(property, nodeKey);

        return _nodes.Where(Equal(property, snapshot));
    }

    /// <summary>Builds typed parameter equality while keeping the configured column's store semantics.</summary>
    private static Expression<Func<TEntity, bool>> Equal<TValue>(
        IProperty property,
        TValue value
    )
        where TValue : notnull => NestedSetKeyFilter<TEntity>.Equal(property, value);

    /// <summary>Adds a cumulative owned identity without visiting application predicates.</summary>
    private Expression<Func<TEntity, bool>> Bind<TValue>(
        IProperty property,
        TValue value
    )
        where TValue : notnull
    {
        var parameter = _binding?.Parameters[0] ?? Expression.Parameter(typeof(TEntity), "node");
        var comparison = NestedSetKeyFilter<TEntity>.Equal(parameter, property, value);
        var condition = _binding is null ? comparison : Expression.AndAlso(_binding.Body, comparison);

        // WHY: EF revisits every root occurrence during warm parameter extraction. One cumulative
        // owned Where removes repeated method calls while preserving exact-root provenance and the
        // original snapshots, including contradictory rebindings. Caller filters never enter this state.
        return Expression.Lambda<Func<TEntity, bool>>(condition, parameter);
    }

    /// <summary>Returns the mapped Int64 left-bound selector used for stable preorder.</summary>
    private Expression<Func<TEntity, long>> Left()
    {
        var property = _descriptor.Left.Name;

        return node => EF.Property<long>(node, property);
    }

    /// <summary>Returns the mapped Int64 sibling-position selector.</summary>
    private Expression<Func<TEntity, long>> Position()
    {
        var property = _descriptor.Position.Name;

        return node => EF.Property<long>(node, property);
    }

    /// <summary>Rejects public query creation until the configured Scope is explicitly bound.</summary>
    private void RequireScope()
    {
        if (!_scopeBound)
        {
            throw new InvalidOperationException(
                $"Hierarchy '{_entityType.Name}' defines Scope '{_descriptor.Scope!.Value.Name}'. "
                + "Call ForScope() first.");
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
                $"{role} for hierarchy '{_entityType.Name}' has model type '{property.ClrType.FullName}', "
                + $"but the argument has type '{typeof(TValue).FullName}'.",
                role);
        }
    }
}
