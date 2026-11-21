namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    /// <summary>Marks owners that must be Deleted while EF reinstalls a Deleted element in its current slot.</summary>
    /// <returns>Whether the entity itself is such an owner.</returns>
    /// <remarks>
    ///     EF keeps a Deleted element's current slot only when its owner was Deleted at that moment, and it leaves
    ///     the element alone when that owner returns to life. Replaying that order is the only native way to
    ///     rebuild this state, which a removed and then resurrected entity produces.
    /// </remarks>
    private bool PrepareDeletedHolds()
    {
        var holdsEntity = false;
        Dictionary<object, ElementSnapshot>? elements = null;

        // WHY: Index the stable, operation-owned collection frames in capture order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var collectionIndex = 0; collectionIndex < _collections.Count; collectionIndex++)
        {
            var collection = _collections[collectionIndex];

            if (!collection.RetainsDeletedElement)
            {
                continue;
            }

            if (ReferenceEquals(collection.Owner, _owner))
            {
                holdsEntity |= NestedSetComplexEntryAccess.State(_owner) != EntityState.Deleted;

                continue;
            }

            elements ??= _collections
                .SelectMany(frame => frame.Elements)
                .ToDictionary(element => element.Native, ReferenceEqualityComparer.Instance);

            elements[collection.Owner]
                .HoldAsDeleted();
        }

        return holdsEntity;
    }

    /// <summary>Makes the entity Deleted without letting EF create original-lane children for it.</summary>
    private void HoldEntityAsDeleted()
    {
        // WHY: Select only this owner from the stable frame list without allocating a filtering iterator.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _collections.Count; index++)
        {
            var collection = _collections[index];

            if (ReferenceEquals(collection.Owner, _owner))
            {
                collection.HideOriginal();
            }
        }

        NestedSetComplexEntryAccess.SetState(_owner, EntityState.Deleted);
    }

    /// <summary>Returns held owners to life innermost first, then settles the states EF cascaded over.</summary>
    private void ReleaseDeletedHolds(
        bool holdsEntity,
        EntityState entityState
    )
    {
        var released = holdsEntity;

        // WHY: Release held owners child-first before settling their cascaded lifecycle states.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = _collections.Count - 1; index >= 0; index--)
        {
            released |= _collections[index]
                .ReleaseHeldOwners();
        }

        if (holdsEntity)
        {
            NestedSetComplexEntryAccess.SetState(_owner, entityState);
        }

        if (!released)
        {
            // WHY: Without a returning owner nothing cascaded. Another transition here would make EF fill
            // captured null slots, and a child's Modified propagation is settled later by the scalar flags.
            return;
        }

        // WHY: Index the stable, operation-owned collection frames in capture order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var collectionIndex = 0; collectionIndex < _collections.Count; collectionIndex++)
        {
            var collection = _collections[collectionIndex];

            collection.SettleStates();
        }
    }

    /// <summary>Runs an entity lifecycle transition, whose recursive flag updates read every element path.</summary>
    /// <param name="transition">The whole-entity state change that EF cascades through the complex graph.</param>
    internal void Transition(
        Action transition
    )
    {
        try
        {
            StageCollections(retiring: false);

            transition();
        }
        finally
        {
            UndoStaging();
            RestoreMembers();
        }

        // WHY: EF's transition grows lanes to raw CLR lists and cascades into their elements. The earlier
        // topology pass cannot foresee that, so it reruns against the settled entity state.
        Restore();
    }

    /// <summary>Removes temporary containers child-first while their containing paths remain reachable.</summary>
    private void UndoStaging()
    {
        // WHY: Staging can visit original/live objects outside the captured current graph. Put their
        // own containers back before the final raw-null replay hides them from containing-entity setters.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = _collections.Count - 1; index >= 0; index--)
        {
            _collections[index]
                .UndoStage(_entry.Entity);
        }
    }

    /// <summary>Removes live native registrations child-first without detecting or changing CLR membership.</summary>
    private static void DetachCollections(
        object owner,
        ITypeBase type,
        HashSet<object> visited,
        HashSet<object> captured
    )
    {
        foreach (var property in type.GetFlattenedComplexProperties())
        {
            if (!property.IsCollection)
            {
                continue;
            }

            DetachCollection(owner, property, visited, captured);
        }
    }

    /// <summary>Removes one collection's registrations without revisiting restored siblings.</summary>
    private static void DetachCollection(
        object owner,
        IComplexProperty property,
        HashSet<object> visited,
        HashSet<object> captured
    )
    {
        var elements = ReadElements(owner, property);

        // WHY: Removed entries can retain ordinals beyond a callback-shortened lane. EF's Detached
        // transition clears those slots directly, so its own capacity operation must make them addressable.
        var currentCount = NestedSetComplexEntryAccess.Entries(owner, property, false)
            .Count;

        var originalCount = NestedSetComplexEntryAccess.Entries(owner, property, true)
            .Count;

        foreach (var element in elements)
        {
            currentCount = Math.Max(currentCount, NestedSetComplexEntryAccess.Ordinal(element, false) + 1);
            originalCount = Math.Max(originalCount, NestedSetComplexEntryAccess.Ordinal(element, true) + 1);
        }

        NestedSetComplexEntryAccess.Capacity(owner, property, currentCount, originalCount);

        foreach (var element in elements)
        {
            if (visited.Add(element))
            {
                if (captured.Contains(element))
                {
                    DetachCollections(element, property.ComplexType, visited, captured);
                    NestedSetComplexEntryAccess.SetState(element, EntityState.Detached);
                }
                else
                {
                    // WHY: Post-capture identities are cache additions, not retained tracker authorities.
                    // Their CLR path may be gone; remove their caches without traversing that missing graph.
                    DiscardCollections(element, property.ComplexType, visited, captured);

                    if (captured.Contains(owner))
                    {
                        // WHY: Journal-owned containing paths have staged access. Keep normal retirement
                        // for their direct additions; deeper discarded owners have no such access authority.
                        NestedSetComplexEntryAccess.SetState(element, EntityState.Detached);
                    }
                }
            }
        }

        // WHY: Added/Deleted transitions can leave opposite-lane pointers behind. A containing entry's
        // next state transition must never cascade into those retired children through stale ordinals.
        NestedSetComplexEntryAccess.Capacity(owner, property, 0, 0);
    }

    /// <summary>Unregisters post-capture complex cache subtrees without reading discarded CLR paths.</summary>
    private static void DiscardCollections(
        object owner,
        ITypeBase type,
        HashSet<object> visited,
        HashSet<object> captured
    )
    {
        foreach (var property in type.GetFlattenedComplexProperties())
        {
            if (!property.IsCollection)
            {
                continue;
            }

            foreach (var element in ReadElements(owner, property))
            {
                // WHY: EF's native ContainingEntry is immutable. A post-capture owner cannot acquire a
                // captured descendant; refuse cache clearing if a changed framework violates that invariant.
                if (captured.Contains(element))
                {
                    throw new InvalidOperationException(
                        "A post-capture complex owner contains a captured native identity.");
                }

                if (visited.Add(element))
                {
                    DiscardCollections(element, property.ComplexType, visited, captured);
                }
            }

            NestedSetComplexEntryAccess.Capacity(owner, property, 0, 0);
        }
    }
}
