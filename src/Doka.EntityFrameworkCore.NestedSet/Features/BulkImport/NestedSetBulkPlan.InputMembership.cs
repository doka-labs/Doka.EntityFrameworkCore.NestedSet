namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    private static readonly ConditionalWeakTable<IProperty, MemberInfo> s_principalMembers = new();
    private int _destinationDepth;
    private NestedSetParent<TKey> _destinationParent;
    private bool _hasDestination;

    /// <summary>Shares one lazy exact-reference index across every wave and tree of a validated import.</summary>
    /// <param name="plans">The immutable plans retained by the existing subtree or forest operation.</param>
    /// <returns>A proof that authorizes no entity state transition and executes no database command.</returns>
    internal static NestedSetImportInputs CreateImportInputs(
        IEnumerable<NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>> plans
    )
    {
        // WHY: Callers retain an array or its repeatable plan projection. Read one plan now without copying
        // the complete sequence before the lazy input index is requested.
        // ReSharper disable once PossibleMultipleEnumeration
        var first = plans.First();
        Dictionary<object, (NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> Plan, Node Node)>? inputs = null;

        var keys = first
            ._store
            .Map
            .EntityType
            .GetDerivedTypesInclusive()
            .SelectMany(type => type.GetForeignKeys())
            .Where(key => key is { IsOwnership: false, PrincipalToDependent.IsCollection: true })
            .ToHashSet();

        return new NestedSetImportInputs(Matches, Mapping, keys, first._store.Map.EntityType);

        bool Matches(
            IUpdateEntry principal,
            IForeignKey key,
            object entity
        ) => Mapping(entity) is not null
            && inputs!.TryGetValue(entity, out var input)
            && input.Plan.MatchesInputOwner(input.Node, principal, key);

        IEntityType? Mapping(
            object entity
        )
        {
            if (entity is not TEntity node
                || first._knownTracked.Contains(node))
            {
                return null;
            }

            // WHY: Ordinary million-node imports have no prepared external collection. Build no second
            // plan-wide index unless a navigation actually asks about an untracked input reference.
            // ReSharper disable once PossibleMultipleEnumeration
            inputs ??= IndexInputs(plans);

            return inputs.TryGetValue(entity, out var input)
                ? input.Plan.InputMapping(input.Node)
                : null;
        }
    }

    /// <summary>Indexes existing plan nodes once without creating future EF entries or per-row receipts.</summary>
    private static Dictionary<object, (NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope> Plan, Node Node)> IndexInputs(
        IEnumerable<NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>> plans
    )
    {
        // WHY: The retained plan source is repeatable. Sum node counts first to size the dictionary once,
        // then index the existing nodes without allocating another plan array.
        // ReSharper disable once PossibleMultipleEnumeration
        var count = plans.Sum(plan => plan.Nodes.Count);
        var result = new Dictionary<object, (NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>, Node)>(
            count,
            ReferenceEqualityComparer.Instance);

        // ReSharper disable once PossibleMultipleEnumeration
        foreach (var plan in plans)
        {
            foreach (var node in plan.Nodes)
            {
                result.Add(node.Entity, (plan, node));
            }
        }

        return result;
    }

    /// <summary>Checks exact input mapping and future persisted relationships, including unstaged roles.</summary>
    private bool MatchesInputOwner(
        Node node,
        IUpdateEntry principal,
        IForeignKey foreignKey
    )
    {
        var mapped = InputMapping(node);

        if (mapped is null
            || !foreignKey.DeclaringEntityType.IsAssignableFrom(mapped)
            || NestedSetModelMapping
                .For(mapped.Model)
                .Owner(mapped)
            != _store.Map.EntityType)
        {
            return false;
        }

        var contract = NestedSetInsertionContract.Instance;
        var entry = contract.FindTrackedEntry(contract.Manager(principal), node.Entity, mapped);

        if (entry is { EntityState: not EntityState.Detached })
        {
            // WHY: Whole-input membership never authorizes an early Add or a write outside the active batch.
            return false;
        }

        // WHY: Principal-key and FK ordinals correspond. Staging owns Scope/TreeId, so raw input sentinels
        // cannot prove membership in another owner or another tree.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < foreignKey.Properties.Count; index++)
        {
            var property = foreignKey.Properties[index];
            var key = foreignKey.PrincipalKey.Properties[index];

            if (property == _store.Map.ScopeProperty)
            {
                if (!MatchesPrincipalValue(principal, key, _stagedScope))
                {
                    return false;
                }
            }
            else if (property == _store.Map.TreeIdProperty)
            {
                if (!MatchesPrincipalValue(principal, key, _stagedTreeId))
                {
                    return false;
                }
            }
            else if (!TryInputValue(node, entry, property, out var value)
                || value is null
                || !key
                    .GetKeyValueComparer()
                    .Equals(value, principal.GetCurrentValue(key)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Uses the same runtime or explicitly named mapping selected by normal staging.</summary>
    private IEntityType? InputMapping(
        Node node
    ) => _store.Map.EntityType.HasSharedClrType
        ? _store.Map.EntityType
        : _store.Context.Model.FindRuntimeEntityType(node.Entity.GetType());

    /// <summary>Keeps Scope/TreeId proofs typed while retaining unusual member-conversion semantics.</summary>
    private static bool MatchesPrincipalValue<TValue>(
        IUpdateEntry entry,
        IProperty property,
        TValue expected
    )
    {
        // WHY: Repeating EF's member-access resolution allocated 336 bytes per future-input proof. Cache only
        // immutable getter metadata; current principal values and configured equality remain live on every call.
        var typed = property.IsShadowProperty()
            || s_principalMembers.GetValue(property, static metadata => metadata.GetMemberInfo(false, false)) switch
            {
                FieldInfo field => field.FieldType == typeof(TValue),
                PropertyInfo member => member.PropertyType == typeof(TValue),
                _ => false,
            };

        if (typed && property.GetKeyValueComparer() is ValueComparer<TValue> comparer)
        {
            var current = entry.GetCurrentValue<TValue>(property);

            return expected is null
                ? current is null
                : current is not null && comparer.Equals(current, expected);
        }

        return property
            .GetKeyValueComparer()
            .Equals(expected, entry.GetCurrentValue(property));
    }

    /// <summary>Reads future values proven by the plan or the caller's existing payload storage.</summary>
    private bool TryInputValue(
        Node node,
        IUpdateEntry? entry,
        IProperty property,
        out object? value
    )
    {
        if (property == _store.Map.KeyProperty)
        {
            value = node.CurrentKey;

            return node.HasAssignedKey || !NestedSetStructuralValue.Matches(property, value, property.Sentinel);
        }

        if (property == _store.Map.ParentProperty)
        {
            var parent = node.Parent < 0
                ? _destinationParent
                : new NestedSetParent<TKey>(true, Nodes[node.Parent].CurrentKey);

            value = parent.BoxedValue;

            return _hasDestination
                && (node.Parent < 0
                    || Nodes[node.Parent].HasAssignedKey
                    || !NestedSetStructuralValue.Matches(
                        _store.Map.KeyProperty,
                        value,
                        _store.Map.KeyProperty.Sentinel));
        }

        if (property == _store.Map.DepthProperty)
        {
            value = checked(_destinationDepth + node.RelativeDepth);

            return _hasDestination;
        }

        if (property == _store.Map.LeftProperty
            || property == _store.Map.RightProperty
            || property == _store.Map.PositionProperty)
        {
            value = property == _store.Map.LeftProperty
                ? node.Geometry.Left
                : property == _store.Map.RightProperty
                    ? node.Geometry.Right
                    : node.Geometry.Position;

            // WHY: Database ordering is finalized after generated values exist; an unstable future coordinate
            // cannot prove an external FK. Manual geometry is already fixed before the first payload wave.
            return _hasDestination && _store.Map.Order is null;
        }

        if (property.IsShadowProperty())
        {
            value = entry?.GetCurrentValue(property) ?? property.Sentinel;

            return property.ValueGenerated == ValueGenerated.Never
                || !NestedSetStructuralValue.Matches(property, value, property.Sentinel);
        }

        var getter = property.GetGetter();
        value = getter.GetClrValueUsingContainingEntity(node.Entity);

        return (property.ValueGenerated & ValueGenerated.OnAdd) == 0
            || !getter.HasSentinelValueUsingContainingEntity(node.Entity);
    }
}
