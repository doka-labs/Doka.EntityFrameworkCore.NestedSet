namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Restores validated input navigation emptiness without retaining entries from completed waves.</summary>
internal sealed class NestedSetInputNavigations
{
    private static readonly ConditionalWeakTable<IEntityType, Shape> s_shapes = new();

    private readonly Dictionary<(object Entity, INavigationBase Navigation), object?> _collections =
        new(new CollectionKeyComparer());

    /// <summary>Temporarily captures collection references for the current bounded tracking wave.</summary>
    internal static void Capture(
        EntityEntry entry,
        ref NestedSetInputNavigations? snapshots
    )
    {
        foreach (var navigation in s_shapes.GetValue(entry.Metadata, CreateShape)
            .Empty)
        {
            if (navigation.IsCollection)
            {
                (snapshots ??= new NestedSetInputNavigations())._collections.TryAdd(
                    (entry.Entity, navigation),
                    navigation
                        .GetGetter()
                        .GetClrValueUsingContainingEntity(entry.Entity));
            }
        }
    }

    /// <summary>Retains only collection initializations or replacements that actually need undoing.</summary>
    /// <param name="identity">One persisted input whose captured navigation references are completed.</param>
    internal void Complete(
        NestedSetInsertionTracking.Identity identity
    )
    {
        var entry = identity.UpdateEntry;
        var entity = entry.ToEntityEntry()
            .Entity;

        foreach (var navigation in s_shapes.GetValue(entry.EntityType, CreateShape)
            .Empty)
        {
            var key = (entity, navigation);

            if (navigation.IsCollection
                && _collections.TryGetValue(key, out var original)
                && ReferenceEquals(
                    navigation
                        .GetGetter()
                        .GetClrValueUsingContainingEntity(entity),
                    original))
            {
                // WHY: EF preserves an existing empty collection instance. Keep no per-row receipt for that
                // unchanged case; only a real initialization/replacement survives the completed wave.
                _collections.Remove(key);
            }
        }
    }

    /// <summary>Restores sparse original collection references after streamed input cleanup.</summary>
    internal void Restore()
    {
        foreach (var ((entity, navigation), original) in _collections)
        {
            EmptyCollection(original, navigation);

            if (!ReferenceEquals(
                navigation
                    .GetGetter()
                    .GetClrValueUsingContainingEntity(entity),
                original))
            {
                NestedSetInsertionContract
                    .Instance
                    .Setter(navigation)
                    .SetClrValueUsingContainingEntity(entity, original);
            }
        }
    }

    /// <summary>Clears library-created non-owned links on validated inputs, including earlier detached waves.</summary>
    internal static void RestoreEmpty(
        IEntityType entityType,
        object entity,
        bool includeOwned = true
    )
    {
        var root = s_shapes.GetValue(entityType, CreateShape);

        if (root.Empty.Length == 0
            && (!includeOwned || root.Owned.Length == 0))
        {
            return;
        }

        var pending = new Stack<(IEntityType Type, object Entity)>();
        pending.Push((entityType, entity));

        while (pending.TryPop(out var current))
        {
            var shape = s_shapes.GetValue(current.Type, CreateShape);

            foreach (var navigation in shape.Empty)
            {
                var value = navigation
                    .GetGetter()
                    .GetClrValueUsingContainingEntity(current.Entity);

                if (navigation.IsCollection)
                {
                    EmptyCollection(value, navigation);
                }
                else if (value is not null)
                {
                    // WHY: Preflight proved these references null. Tracking may populate them even on rows
                    // detached by an earlier successful wave; keeping them would make an identical retry invalid.
                    NestedSetInsertionContract
                        .Instance
                        .Setter(navigation)
                        .SetClrValueUsingContainingEntity(current.Entity, null);
                }
            }

            foreach (var navigation in includeOwned
                ? shape.Owned
                : [])
            {
                var value = navigation
                    .GetGetter()
                    .GetClrValueUsingContainingEntity(current.Entity);

                if (navigation.IsCollection
                    && value is IEnumerable values)
                {
                    foreach (var item in values)
                    {
                        pending.Push((navigation.TargetEntityType, item));
                    }
                }
                else if (value is not null)
                {
                    pending.Push((navigation.TargetEntityType, value));
                }
            }
        }
    }

    /// <summary>Removes individual members so notification collections never receive an unsupported Reset.</summary>
    private static void EmptyCollection(
        object? collection,
        INavigationBase navigation
    )
    {
        if (collection is IList { IsReadOnly: false, IsFixedSize: false } list)
        {
            // WHY: Reverse removal preserves the collection instance and uses ordinary notification events.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = list.Count - 1; index >= 0; index--)
            {
                list.RemoveAt(index);
            }
        }
        else if (collection is IEnumerable values)
        {
            foreach (var member in values
                .Cast<object>()
                .ToArray())
            {
                navigation.GetCollectionAccessor()!.RemoveStandalone(collection, member);
            }
        }
    }

    /// <summary>Separates validated empty non-owned links from retained owned aggregate traversal.</summary>
    private static Shape CreateShape(
        IEntityType type
    )
    {
        var navigations = type
            .GetNavigations()
            .ToArray();

        return new Shape(
            [
                .. navigations.Where(navigation => !navigation.ForeignKey.IsOwnership),
                .. type.GetSkipNavigations(),
            ],
            navigations
                .Where(navigation => navigation.ForeignKey.IsOwnership && !navigation.IsOnDependent)
                .ToArray());
    }

    /// <summary>Retains immutable metadata only; no node or context survives a completed operation.</summary>
    private sealed record Shape(
        INavigationBase[] Empty,
        INavigation[] Owned
    );

    /// <summary>Distinguishes equal-key input objects and named navigation mappings by reference.</summary>
    private sealed class CollectionKeyComparer : IEqualityComparer<(object Entity, INavigationBase Navigation)>
    {
        /// <inheritdoc />
        public bool Equals(
            (object Entity, INavigationBase Navigation) left,
            (object Entity, INavigationBase Navigation) right
        ) => ReferenceEquals(left.Entity, right.Entity) && ReferenceEquals(left.Navigation, right.Navigation);

        /// <inheritdoc />
        public int GetHashCode(
            (object Entity, INavigationBase Navigation) key
        ) => HashCode.Combine(RuntimeHelpers.GetHashCode(key.Entity), RuntimeHelpers.GetHashCode(key.Navigation));
    }
}
