namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

/// <summary>Validates an input forest and retains compact topology, geometry, and structural rollback values.</summary>
/// <typeparam name="TEntity">The detached hierarchy entity type.</typeparam>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
/// <typeparam name="TTreeId">The mapped tree identity type.</typeparam>
/// <typeparam name="TScope">The mapped scope type.</typeparam>
internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;
    private readonly IProperty[] _generatedProperties;
    private readonly IClrPropertySetter[] _generatedSetters;
    private readonly bool _parentHasNoNavigations;
    private readonly bool _parentHasDependency;
    private readonly bool _hasInputNavigations;
    private readonly TScope _stagedScope;
    private readonly TTreeId _stagedTreeId;
    private readonly HashSet<TEntity> _knownTracked;
    private EntityEntry[]? _activeEntries;
    private Dictionary<IUpdateEntry, NestedSetInsertionTracking.Identity>? _activeIdentities;
    private List<(EntityEntry Owner, INavigationBase Navigation, object Entity)>? _activeRelationships;
    private List<NestedSetInsertionValues.Snapshot>? _ownedValues;
    private EntityEntry<TEntity>[]? _activeRoots;
    private int _activeOffset;

    /// <summary>Validates the complete topology before the executor can open a database write boundary.</summary>
    /// <param name="store">The exact-tree metadata and context used to inspect detached entries.</param>
    /// <param name="roots">The immutable branch roots requested for this import.</param>
    /// <param name="knownTracked">The shared immutable snapshot of pre-import hierarchy identities.</param>
    /// <param name="cancellationToken">The token checked during potentially deep input traversal.</param>
    internal NestedSetBulkPlan(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        IReadOnlyList<NestedSetBranch<TEntity>> roots,
        HashSet<TEntity> knownTracked,
        CancellationToken cancellationToken
    )
    {
        _store = store;
        _knownTracked = knownTracked;
        _hasInputNavigations = store
            .Map
            .EntityType
            .GetDerivedTypesInclusive()
            .Any(type => type.GetNavigations().Any()
                || type.GetSkipNavigations().Any());

        _generatedProperties = NestedSetRefreshProperties
            .Generated(store.Map.EntityType)
            .Where(property => property != store.Map.KeyProperty
                && property != store.Map.ScopeProperty
                && property != store.Map.TreeIdProperty
                && property != store.Map.ParentProperty
                && property != store.Map.LeftProperty
                && property != store.Map.RightProperty
                && property != store.Map.DepthProperty
                && property != store.Map.PositionProperty
                && !property.IsShadowProperty())
            .ToArray();

        // WHY: Resolve generated CLR access before writes. Rollback must not recreate detached EF entries or
        // discover an unsupported metadata setter only after the database has assigned caller-owned values.
        _generatedSetters = _generatedProperties
            .Select(property => NestedSetDetachedValueSetter.Get(property)!)
            .ToArray();

        _stagedScope = store.Map.ScopeProperty is { } scope
            ? NestedSetTypedValue<TScope>.Snapshot(scope, store.Scope)
            : store.Scope;

        _stagedTreeId = NestedSetTypedValue<TTreeId>.Snapshot(store.Map.TreeIdProperty, store.TreeId);

        var keyProperty = store.Map.KeyProperty;
        var parentKeys = store
            .Map
            .ParentProperty
            .GetContainingForeignKeys()
            .ToArray();

        _parentHasNoNavigations = parentKeys.All(foreignKey => foreignKey.DependentToPrincipal is null
            && foreignKey.PrincipalToDependent is null);

        _parentHasDependency = parentKeys.Any(foreignKey => IsParentDependency(
            foreignKey,
            store.Map.EntityType,
            store.Map.ParentProperty,
            keyProperty));

        var pending = new Stack<(NestedSetBranch<TEntity> Branch, int Parent)>();
        var seen = new HashSet<TEntity>(ReferenceEqualityComparer.Instance);
        var assignedKeys = new HashSet<TKey>(store.Map.KeyComparer);
        for (var index = roots.Count - 1; index >= 0; index--)
        {
            ArgumentNullException.ThrowIfNull(roots[index]);
            pending.Push((roots[index], -1));
        }

        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: Repeated entity references describe cycles or multiple parents; neither has a tree meaning.
            if (!seen.Add(item.Branch.Entity))
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidImport,
                    "Each imported entity must occur exactly once in the forest.");
            }

            RequireDetached(item.Branch.Entity, knownTracked, store.Map.EntityType);
            var key = NestedSetTypedValue<TKey>.Read(item.Branch.Entity, keyProperty);

            var assignedKey = keyProperty.ValueGenerated == ValueGenerated.Never
                || !keyProperty
                    .GetValueComparer()
                    .Equals(key, keyProperty.Sentinel);

            if (assignedKey && !assignedKeys.Add(key))
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidImport,
                    "Imported entities must have distinct assigned primary keys.");
            }

            var node = new Node(
                item.Branch.Entity,
                store.Map,
                item.Parent,
                item.Parent < 0 ? 0 : checked(Nodes[item.Parent].RelativeDepth + 1),
                assignedKey);

            var nodeIndex = Nodes.Count;
            Nodes.Add(node);

            if (item.Parent < 0)
            {
                Roots.Add(nodeIndex);
            }
            else
            {
                AppendChild(item.Parent, nodeIndex);
            }

            for (var index = item.Branch.Children.Count - 1; index >= 0; index--)
            {
                pending.Push((item.Branch.Children[index], nodeIndex));
            }
        }

        Width = checked((long)Nodes.Count * 2);
    }

    /// <summary>Captures existing hierarchy identities once for all branches in one insertion boundary.</summary>
    /// <param name="context">The context whose pre-import tracked entries must remain present.</param>
    /// <param name="entityType">The exact mapped hierarchy type.</param>
    /// <returns>A reference-identity snapshot shared immutably by the prepared plans.</returns>
    internal static HashSet<TEntity> CaptureKnownTracked(
        DbContext context,
        IEntityType entityType
    )
    {
        var tracker = context.ChangeTracker;
        var automaticDetection = tracker.AutoDetectChangesEnabled;

        try
        {
            // WHY: This baseline needs tracked identities only. The executor detects pending CLR changes
            // before the first write; repeating detection here scans unrelated application payload twice.
            tracker.AutoDetectChangesEnabled = false;

            return NestedSetEntityAccess<TEntity>
                .Entries(context, entityType)
                .Where(entry => entry.State != EntityState.Detached)
                .Select(entry => entry.Entity)
                .ToHashSet<TEntity>(ReferenceEqualityComparer.Instance);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = automaticDetection;
        }
    }

    /// <summary>Appends one child without allocating a collection on every imported node.</summary>
    private void AppendChild(
        int parentIndex,
        int childIndex
    )
    {
        var parent = Nodes[parentIndex];

        if (parent.LastChild < 0)
        {
            parent.FirstChild = childIndex;
        }
        else
        {
            Nodes[parent.LastChild].NextSibling = childIndex;
        }

        parent.LastChild = childIndex;
        parent.ChildCount++;
    }

    /// <summary>Gets each input entity once, with compact structural state and its parent index.</summary>
    internal List<Node> Nodes { get; } = [];

    /// <summary>Gets the indices of new sibling roots at the destination.</summary>
    internal List<int> Roots { get; } = [];

    /// <summary>Gets the single interval width reserved for the whole import.</summary>
    internal long Width { get; }

    /// <summary>Rejects impossible destination depths and sibling positions before any structural write.</summary>
    internal void RequireCapacity(
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination
    )
    {
        _ = checked(destination.Position + Roots.Count);
        _ = checked(destination.Depth + Nodes.Max(node => node.RelativeDepth));
    }
}
