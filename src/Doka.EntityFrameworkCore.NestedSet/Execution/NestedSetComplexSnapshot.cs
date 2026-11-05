namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Journals caller-owned complex containers separately from EF's current and original element lanes.</summary>
internal sealed partial class NestedSetComplexSnapshot
{
    private readonly List<CollectionSnapshot> _collections = [];
    private readonly EntityEntry _entry;
    private readonly List<MemberSnapshot> _members = [];
    private readonly object _owner;

    /// <summary>Captures complex topology without detecting raw changes or replacing caller instances.</summary>
    /// <param name="entry">The entity whose complex graph shares the coordinated write.</param>
    /// <param name="owner">The same entity's existing native update identity.</param>
    internal NestedSetComplexSnapshot(
        EntityEntry entry,
        IUpdateEntry owner
    )
    {
        _entry = entry;
        _owner = owner;
        CaptureMembers(entry.Metadata, entry.Entity, []);
        CaptureCollections(owner, entry.Metadata, entry.Entity, []);
    }

    /// <summary>Reinstates containers and native positions before any captured scalar wrapper is read.</summary>
    internal void Restore()
    {
        if (_collections.All(collection => collection.MatchesLanes()))
        {
            // WHY: Raw callback edits do not change native identities. Rebinding every unchanged element
            // would run EF's full-list transition checks repeatedly; only its independent originals need replay.
            RestoreMembers();

            foreach (var collection in _collections)
            {
                collection.RestoreOriginal();
            }

            return;
        }

        var captured = new HashSet<object>(ReferenceEqualityComparer.Instance) { _owner };

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var collection in _collections)
        {
            foreach (var element in collection.Elements)
            {
                captured.Add(element.Native);
            }
        }

        foreach (var collection in _collections)
        {
            collection.CaptureLiveReferences(_entry.Entity);
        }

        RestoreMembers();

        try
        {
            // WHY: Retirement walks callback-shifted live ordinals, while reinstallation walks captured ones.
            // Each phase needs its own temporary access graph; sharing one lets EF index the wrong element.
            foreach (var collection in _collections)
            {
                collection.Stage(_entry.Entity, retiring: true);
            }

            DetachCollections(
                _owner,
                _entry.Metadata,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                captured);

            foreach (var collection in _collections)
            {
                collection.DetachRetained(captured);
            }

            UndoStaging();

            foreach (var collection in _collections)
            {
                collection.RestoreOrdinals();
            }

            var holdsEntity = PrepareDeletedHolds();

            foreach (var collection in _collections)
            {
                collection.Stage(_entry.Entity, retiring: false);
            }

            foreach (var collection in _collections)
            {
                // WHY: A containing transition initializes child entries before their local frame runs.
                // New snapshots must see captured originals rather than accepted callback collection buffers.
                collection.RestoreOriginal();
            }

            var entityState = NestedSetComplexEntryAccess.State(_owner);

            if (holdsEntity)
            {
                HoldEntityAsDeleted();
            }

            foreach (var collection in _collections)
            {
                collection.Restore(captured);
            }

            ReleaseDeletedHolds(holdsEntity, entityState);

            foreach (var collection in _collections)
            {
                collection.RestoreHiddenProperties();
            }

            foreach (var collection in _collections)
            {
                collection.ReleaseAddresses();
            }
        }
        finally
        {
            UndoStaging();
            RestoreMembers();

            // WHY: EF grows native lanes to the temporary access graph during state callbacks. Its final
            // lengths belong to the captured preimage, including legitimate trailing null ordinal slots.
            foreach (var collection in _collections)
            {
                collection.RestoreCapacity();
            }
        }
    }

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

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var collection in _collections)
        {
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
        foreach (var collection in _collections.Where(frame => ReferenceEquals(frame.Owner, _owner)))
        {
            collection.HideOriginal();
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

        foreach (var collection in _collections)
        {
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
            foreach (var collection in _collections)
            {
                collection.Stage(_entry.Entity, retiring: false);
            }

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

    /// <summary>Replays the exact CLR preimage after native or scalar setters have visited containing values.</summary>
    internal void RestoreMembers()
    {
        foreach (var member in _members)
        {
            member.Restore(_entry.Entity);
        }
    }

    /// <summary>Replays hidden scalars after a container replay put an old struct box back.</summary>
    internal void RestoreHiddenValues()
    {
        foreach (var collection in _collections)
        {
            collection.RestoreHiddenValues();
        }
    }

    /// <summary>Restores aggregate intent after scalar and entity state transitions have settled.</summary>
    internal void RestoreFlags()
    {
        // WHY: Aggregate notifications propagate to containing entries. Child flags settle before parents;
        // the native non-recursive setter preserves independently captured Added and Deleted element states.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = _collections.Count - 1; index >= 0; index--)
        {
            _collections[index]
                .RestoreFlag();
        }
    }

    /// <summary>Rejects incomplete graph recovery before the context can be reused.</summary>
    internal void Verify()
    {
        foreach (var member in _members)
        {
            member.Verify(_entry.Entity);
        }

        foreach (var collection in _collections)
        {
            collection.Verify();
        }
    }

    /// <summary>Captures containers in parent-first order, including null boundaries and fixed-size lists.</summary>
    private void CaptureMembers(
        ITypeBase type,
        object instance,
        int[] ordinals
    )
    {
        foreach (var property in type.GetComplexProperties())
        {
            var value = property
                .GetGetter()
                .GetClrValue(instance);

            _members.Add(new MemberSnapshot(property, value, ordinals));

            if (!property.IsCollection)
            {
                if (value is not null)
                {
                    CaptureMembers(property.ComplexType, value, ordinals);
                }

                continue;
            }

            if (value is not IList list
                || !property
                    .ComplexType
                    .GetComplexProperties()
                    .Any())
            {
                continue;
            }

            // WHY: Captured ordinal paths let EF's mapped setter copy nested value-type references back
            // through their containers; assigning a boxed struct's reflected member would lose that write.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < list.Count; index++)
            {
                if (list[index] is null)
                {
                    continue;
                }

                var path = new int[ordinals.Length + 1];
                ordinals.CopyTo(path, 0);
                path[^1] = index;
                CaptureMembers(property.ComplexType, list[index]!, path);
            }
        }
    }

    /// <summary>Includes original-only Deleted entries and current slots beyond a raw shortened CLR list.</summary>
    private void CaptureCollections(
        object owner,
        ITypeBase type,
        object? instance,
        int[] path
    )
    {
        foreach (var property in type.GetFlattenedComplexProperties())
        {
            if (!property.IsCollection)
            {
                continue;
            }

            var container = DeclaringInstance(type, instance, property.DeclaringType);
            var value = container is null
                ? null
                : property
                    .GetGetter()
                    .GetClrValue(container);

            var snapshot = new CollectionSnapshot(owner, property, value as IList, path);
            _collections.Add(snapshot);

            if (!property
                .ComplexType
                .GetFlattenedComplexProperties()
                .Any(nested => nested.IsCollection))
            {
                continue;
            }

            foreach (var element in snapshot.Elements)
            {
                // WHY: Native element ownership is acyclic and unique to this collection. Its two lanes
                // are deduplicated by the frame, so no graph-sized visited set is needed during capture.
                var childPath = new int[path.Length + 1];
                path.CopyTo(childPath, 0);
                childPath[^1] = element.CurrentSlot;
                CaptureCollections(
                    element.Native,
                    property.ComplexType,
                    snapshot.CurrentItem(element.CurrentSlot),
                    childPath);
            }
        }
    }

    /// <summary>Reads a flattened reference without accessing a missing current ordinal.</summary>
    private static object? DeclaringInstance(
        ITypeBase ownerType,
        object? instance,
        ITypeBase declaringType
    )
    {
        if (instance is null
            || ownerType == declaringType
            || declaringType is not IComplexType complex)
        {
            return instance;
        }

        var property = complex.ComplexProperty;
        var parent = DeclaringInstance(ownerType, instance, property.DeclaringType);

        return parent is null
            ? null
            : property
                .GetGetter()
                .GetClrValue(parent);
    }

    /// <summary>Reads a potentially absent callback path without invoking EF's throwing ordinal accessor.</summary>
    private static object? ReadContaining(
        ITypeBase type,
        object root,
        int[] path,
        ref int depth,
        List<(IComplexProperty Property, object Instance)>? references = null
    )
    {
        if (type is not IComplexType complex)
        {
            return root;
        }

        var property = complex.ComplexProperty;
        var parent = ReadContaining(property.DeclaringType, root, path, ref depth, references);

        if (parent is null)
        {
            return null;
        }

        var value = property
            .GetGetter()
            .GetClrValue(parent);

        if (!property.IsCollection)
        {
            if (value is not null)
            {
                references?.Add((property, value));
            }

            return value;
        }

        var ordinal = depth < path.Length
            ? path[depth++]
            : -1;

        return value is IList list && (uint)ordinal < (uint)list.Count
            ? list[ordinal]
            : null;
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

    /// <summary>Reads both native lanes before state callbacks can mutate either list.</summary>
    private static object[] ReadElements(
        object owner,
        IComplexProperty property
    )
    {
        var current = NestedSetComplexEntryAccess.Entries(owner, property, false);
        var original = NestedSetComplexEntryAccess.Entries(owner, property, true);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var elements = new List<object>(current.Count + original.Count);

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var lane in new[] { current, original })
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var element in lane)
            {
                if (element is not null
                    && seen.Add(element))
                {
                    elements.Add(element);
                }
            }
        }

        return elements.ToArray();
    }

    /// <summary>Retains one native collection's current/original positional authorities and aggregate flag.</summary>
    private sealed class CollectionSnapshot
    {
        private readonly int _currentCount;
        private readonly int _currentElements;
        private readonly List<HiddenPropertySnapshot>? _hiddenProperties;
        private readonly object?[]? _items;
        private readonly bool _modified;
        private readonly object? _original;
        private readonly int _originalCount;
        private readonly int _originalElements;
        private readonly List<(IComplexProperty Property, object Instance)>? _originalReferences;
        private readonly int[] _path;
        private readonly IComplexProperty _property;
        private readonly List<RawScalarSnapshot>? _rawScalars;
        private List<(IComplexProperty Property, object Instance)>? _liveReferences;
        private StagingSnapshot? _staging;

        /// <summary>Copies native element identities and positions independently of mutable CLR membership.</summary>
        internal CollectionSnapshot(
            object owner,
            IComplexProperty property,
            IList? currentValues,
            int[] path
        )
        {
            Owner = owner;
            _property = property;
            _path = path;

            var type = property.DeclaringType;

            while (currentValues is null
                && type is IComplexType { ComplexProperty.IsCollection: false } complex)
            {
                if (complex.ComplexProperty.IsNullable
                    && NestedSetComplexEntryAccess.Original(owner, complex.ComplexProperty) is { } reference)
                {
                    (_originalReferences ??= []).Add((complex.ComplexProperty, reference));
                }

                type = complex.ComplexProperty.DeclaringType;
            }

            _originalReferences?.Reverse();

            if (currentValues is not null)
            {
                _items = new object?[currentValues.Count];
                currentValues.CopyTo(_items, 0);
            }

            _original = NestedSetComplexEntryAccess.Original(owner, property);
            _currentCount = NestedSetComplexEntryAccess.Entries(owner, property, false)
                .Count;

            _originalCount = NestedSetComplexEntryAccess.Entries(owner, property, true)
                .Count;

            _modified = NestedSetComplexEntryAccess.Collection(owner, property)
                .IsModified;

            var current = NestedSetComplexEntryAccess.Entries(owner, property, false);
            var original = NestedSetComplexEntryAccess.Entries(owner, property, true);

            if (TryCaptureAligned(current, original, out var aligned))
            {
                _currentElements = aligned.Length;
                _originalElements = aligned.Length;
                Elements = aligned;
            }
            else
            {
                var elements = new Dictionary<object, ElementSnapshot>(ReferenceEqualityComparer.Instance);

                // WHY: Added/Deleted parents can retain native slots despite minus-one ordinals. Only
                // proven aligned lanes skip the general identity union and its independently captured slots.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < current.Count; index++)
                {
                    if (current[index] is { } native)
                    {
                        _currentElements++;
                        elements.Add(native, new ElementSnapshot(native, index, CurrentItem(index) is not null));
                    }
                }

                // WHY: Original-only Deleted entries have no current CLR item exposing their identity.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < original.Count; index++)
                {
                    if (original[index] is { } native)
                    {
                        _originalElements++;

                        if (!elements.TryGetValue(native, out var element))
                        {
                            element = new ElementSnapshot(native, -1, false);
                            elements.Add(native, element);
                        }

                        element.OriginalSlot = index;
                    }
                }

                Elements = elements.Values.ToArray();
            }

            CaptureRawElements(current, ref _rawScalars);

            // WHY: EF exposes no public entry for a current element below a Deleted owner, so the scalar journal
            // cannot pair it. Its intent and CLR values are retained through this native path instead.
            var deletedOwner = NestedSetComplexEntryAccess.State(owner) == EntityState.Deleted;

            foreach (var element in Elements)
            {
                if (!deletedOwner
                    && CurrentItem(element.CurrentSlot) is not null)
                {
                    continue;
                }

                var originalInstance = CurrentItem(element.CurrentSlot)
                    ?? (_original is IList originals && (uint)element.OriginalSlot < (uint)originals.Count
                        ? originals[element.OriginalSlot]
                        : null);

                foreach (var scalar in property.ComplexType.GetFlattenedProperties())
                {
                    (_hiddenProperties ??= []).Add(
                        new HiddenPropertySnapshot(
                            element.Native,
                            scalar,
                            originalInstance,
                            element.CurrentSlot >= 0 && path.All(ordinal => ordinal >= 0)));
                }
            }
        }

        /// <summary>The exact native elements present in either captured lane.</summary>
        internal ElementSnapshot[] Elements { get; }

        /// <summary>The native entry whose lanes this frame restores.</summary>
        internal object Owner { get; }

        /// <summary>Whether a captured Deleted element also occupies a current slot.</summary>
        internal bool RetainsDeletedElement => Elements.Any(element => element.RetainsDeletedSlot);

        /// <summary>Journals CLR elements that EF has not registered, because no native entry retains them.</summary>
        private void CaptureRawElements(
            IList current,
            ref List<RawScalarSnapshot>? scalars
        )
        {
            if (_items is null)
            {
                return;
            }

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _items.Length; index++)
            {
                if (_items[index] is { } item
                    && (index >= current.Count || current[index] is null))
                {
                    RawScalarSnapshot.Capture(_property.ComplexType, item, item, scalars ??= []);
                }
            }
        }

        /// <summary>Copies proven identical native lanes without allocating a transient identity map.</summary>
        private bool TryCaptureAligned(
            IList current,
            IList original,
            out ElementSnapshot[] elements
        )
        {
            elements = [];

            if (current.Count != original.Count)
            {
                return false;
            }

            var count = 0;

            // WHY: Matching native ordinals prove uniqueness without another reference set. Divergent
            // lanes and Added/Deleted sentinels retain the general union; paired nulls remain ordinal holes.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Count; index++)
            {
                var native = current[index];

                if (!ReferenceEquals(native, original[index]))
                {
                    return false;
                }

                if (native is null)
                {
                    continue;
                }

                if (NestedSetComplexEntryAccess.Ordinal(native, false) != index
                    || NestedSetComplexEntryAccess.Ordinal(native, true) != index)
                {
                    return false;
                }

                count++;
            }

            if (count == 0)
            {
                return true;
            }

            elements = new ElementSnapshot[count];
            var target = 0;

            // WHY: Eligibility established exact size and positional uniqueness. Each retained element
            // still captures state, both ordinals and the independent presence of its current CLR instance.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Count; index++)
            {
                if (current[index] is { } native)
                {
                    elements[target++] = new ElementSnapshot(native, index, CurrentItem(index) is not null)
                    {
                        OriginalSlot = index,
                    };
                }
            }

            return true;
        }

        /// <summary>Checks actual lane identities and states before native rebinding.</summary>
        internal bool MatchesLanes()
        {
            var current = NestedSetComplexEntryAccess.Entries(Owner, _property, false);
            var original = NestedSetComplexEntryAccess.Entries(Owner, _property, true);

            if (current.Count != _currentCount
                || original.Count != _originalCount
                || CountEntries(current) != _currentElements
                || CountEntries(original) != _originalElements
                || (_hiddenProperties?.Any(property => property.HasChanges) ?? false))
            {
                return false;
            }

            // WHY: Compare actual identities without allocating a closure per collection frame.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < Elements.Length; index++)
            {
                if (!Elements[index]
                    .MatchesLanes(current, original))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Counts installed entries separately from null ordinal slots.</summary>
        private static int CountEntries(
            IList entries
        )
        {
            var count = 0;

            // WHY: Native IReadOnlyList backing stores expose IList here. Indexing avoids boxing their
            // enumerator on every graph frame; null slots still belong to the captured positional authority.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index] is not null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Replays an original buffer without changing current CLR membership.</summary>
        internal void RestoreOriginal() => NestedSetComplexEntryAccess.RestoreOriginal(Owner, _property, _original);

        /// <summary>Restores captured access paths before the original buffers and states are replayed.</summary>
        internal void RestoreOrdinals()
        {
            foreach (var element in Elements)
            {
                element.EndRetirement();
            }
        }

        /// <summary>Removes temporary staging capacity after the actual CLR containers are reachable again.</summary>
        internal void RestoreCapacity() =>
            NestedSetComplexEntryAccess.Capacity(Owner, _property, _currentCount, _originalCount);

        /// <summary>Restores hidden scalar intent while its temporary containers are accessible.</summary>
        internal void RestoreHiddenProperties()
        {
            if (_hiddenProperties is null)
            {
                return;
            }

            foreach (var property in _hiddenProperties)
            {
                property.Restore();
            }
        }

        /// <summary>Replays hidden CLR values after containers can have been restored over them.</summary>
        internal void RestoreHiddenValues()
        {
            foreach (var property in _hiddenProperties ?? [])
            {
                property.RestoreClrValue();
            }

            foreach (var scalar in _rawScalars ?? [])
            {
                scalar.Restore();
            }
        }

        /// <summary>Reads a captured current item without indexing a raw-shortened or null list.</summary>
        internal object? CurrentItem(
            int slot
        ) => _items is not null && (uint)slot < (uint)_items.Length
            ? _items[slot]
            : null;

        /// <summary>Retains callback reference containers before restored members can hide native entries.</summary>
        internal void CaptureLiveReferences(
            object root
        )
        {
            if (_property.DeclaringType is IComplexType { ComplexProperty.IsCollection: false })
            {
                // WHY: A Deleted owner's sentinel has no list position, while its CLR slot usually remains.
                var live = LivePath();
                var depth = 0;
                _liveReferences = [];
                ReadContaining(
                    _property.DeclaringType,
                    root,
                    live.Any(ordinal => ordinal < 0)
                        ? _path
                        : live,
                    ref depth,
                    _liveReferences);
            }
        }

        /// <summary>Reads the owner's current native ordinals, which callbacks can have shifted.</summary>
        private int[] LivePath() => NestedSetComplexEntryAccess.Ordinals(Owner);

        /// <summary>Builds an access graph for the ordinals EF will walk during the coming transitions.</summary>
        /// <param name="root">The containing entity whose CLR graph supplies the staged paths.</param>
        /// <param name="retiring">Whether callback entries still exist, so that live references need access.</param>
        internal void Stage(
            object root,
            bool retiring
        )
        {
            var path = LivePath();

            if (path.Any(ordinal => ordinal < 0))
            {
                return;
            }

            var staging = _staging = new StagingSnapshot { Path = path };
            RestoreReferences(root, _originalReferences);

            if (retiring)
            {
                RestoreReferences(root, _liveReferences);
            }

            var depth = 0;
            var parent = ReadContaining(_property.DeclaringType, root, path, ref depth);

            if (parent is null)
            {
                // WHY: EF's getter ends at a missing container before any collection ordinal is indexed.
                return;
            }

            staging.Previous = _property
                .GetGetter()
                .GetClrValue(parent);

            var previous = staging.Previous as IList;
            var count = Math.Max(_currentCount, _originalCount);
            count = Math.Max(count, _items?.Length ?? 0);
            count = Math.Max(count, previous?.Count ?? 0);
            count = Math.Max(
                count,
                NestedSetComplexEntryAccess.Entries(Owner, _property, false)
                    .Count);

            count = Math.Max(
                count,
                NestedSetComplexEntryAccess.Entries(Owner, _property, true)
                    .Count);

            var addressed = Elements
                .Where(element => !retiring
                    || NestedSetComplexEntryAccess.State(element.Native) != EntityState.Detached)
                .ToArray();

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var element in addressed)
            {
                count = Math.Max(count, NestedSetComplexEntryAccess.Ordinal(element.Native, false) + 1);
            }

            if (_property
                .ComplexType
                .GetFlattenedComplexProperties()
                .Any(nested => nested.IsCollection))
            {
                // WHY: EF reads a nested entry through every containing ordinal. A Deleted owner's minus-one
                // sentinel has no list position, so it borrows a free slot until its descendants have settled.
                foreach (var element in addressed)
                {
                    var ordinal = NestedSetComplexEntryAccess.Ordinal(element.Native, false);

                    if (ordinal < 0)
                    {
                        element.Borrow(count++);
                    }
                    else if (!retiring)
                    {
                        element.KeepSlotAcrossDeletion(ordinal);
                    }
                }
            }

            if (count == 0)
            {
                return;
            }

            var items = new object?[count];

            foreach (var element in addressed)
            {
                var ordinal = NestedSetComplexEntryAccess.Ordinal(element.Native, false);

                if (ordinal >= 0
                    && items[ordinal] is null)
                {
                    items[ordinal] = Instance(element);
                }
            }

            var staged = NestedSetComplexEntryAccess.CreateCollection(_property, count);

            // WHY: A mapped custom collection type can reject null even on this private instance. Only slots
            // that lack a CLR element are affected, and they are read for nested counts at most.
            var acceptsNull = staged.GetType() is { IsGenericType: true } type
                && type.GetGenericTypeDefinition() == typeof(List<>);

            var filler = acceptsNull
                ? null
                : Enumerable
                    .Range(0, count)
                    .Select(ItemAt)
                    .FirstOrDefault(item => item is not null);

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < count; index++)
            {
                var item = ItemAt(index) ?? filler;

                if (item is null
                    && !acceptsNull)
                {
                    break;
                }

                staged.Add(item);
            }

            if (previous is not null
                && HasSameItems(previous, staged))
            {
                // WHY: Restored caller storage already supplies the required ordinals. Reassigning it can
                // invoke a copying mapped setter and destroy an otherwise unchanged container identity.
                return;
            }

            Install(root, staged, staging);
            return;

            // WHY: Native state callbacks traverse nested current ordinals even after raw caller removals.
            // Existing captured/original instances supply a temporary access graph, never new entities.
            object? ItemAt(
                int index
            ) => items[index]
                ?? CurrentItem(index)
                ?? (_original is IList original && index < original.Count
                    ? original[index]
                    : null);
        }

        /// <summary>Resolves a retained element's CLR instance from its current or original position.</summary>
        private object? Instance(
            ElementSnapshot element
        ) => CurrentItem(element.CurrentSlot)
            ?? (_original is IList originals && (uint)element.OriginalSlot < (uint)originals.Count
                ? originals[element.OriginalSlot]
                : null);

        /// <summary>Installs temporary access storage without exposing it to caller collection code.</summary>
        private void Install(
            object root,
            IList staged,
            StagingSnapshot staging
        )
        {
            // WHY: A field-mapped setter is EF's direct write, so caller collections stay untouched: temporary
            // null slots raise no events and reach no validating Add. Only a property-mode setter can run code
            // that copies the assigned list; growing its instance is the sole identity-preserving alternative.
            if (_property.GetMemberInfo(forMaterialization: false, forSet: true) is PropertyInfo
                && staging.Previous is IList { IsReadOnly: false, IsFixedSize: false } mutable)
            {
                staging.PreviousItems = new object?[mutable.Count];
                mutable.CopyTo(staging.PreviousItems, 0);
                mutable.Clear();

                foreach (var item in staged)
                {
                    mutable.Add(item);
                }

                return;
            }

            staging.Installed = true;
            NestedSetComplexEntryAccess
                .Setter(_property)
                .SetClrValueUsingContainingEntity(root, staging.Path, staged);
        }

        /// <summary>Checks access-graph identity without invoking caller equality or mapped setters.</summary>
        private static bool HasSameItems(
            IList current,
            IList staged
        )
        {
            if (current.Count != staged.Count)
            {
                return false;
            }

            // WHY: Complex elements have reference identity. Indexed IList access avoids enumerator boxing
            // while proving that skipping the setter preserves every required native ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Count; index++)
            {
                if (!ReferenceEquals(current[index], staged[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Removes temporary containers while their containing current paths remain reachable.</summary>
        internal void UndoStage(
            object root
        )
        {
            var staging = _staging;

            if (staging is null)
            {
                return;
            }

            if (staging.Installed)
            {
                NestedSetComplexEntryAccess
                    .Setter(_property)
                    .SetClrValueUsingContainingEntity(root, staging.Path, staging.Previous);
            }

            if (staging.PreviousItems is { } items)
            {
                var previous = (IList)staging.Previous!;
                previous.Clear();

                foreach (var item in items)
                {
                    previous.Add(item);
                }
            }

            if (staging.References is null)
            {
                _staging = null;

                return;
            }

            // WHY: A nested optional reference must be removed before its temporary parent is hidden.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = staging.References.Count - 1; index >= 0; index--)
            {
                NestedSetComplexEntryAccess
                    .Setter(staging.References[index])
                    .SetClrValueUsingContainingEntity(root, staging.Path, null);
            }

            _staging = null;
        }

        /// <summary>Provides existing reference containers for native cleanup.</summary>
        private void RestoreReferences(
            object root,
            List<(IComplexProperty Property, object Instance)>? references
        )
        {
            if (references is null)
            {
                return;
            }

            var path = _staging!.Path;

            foreach (var (property, instance) in references)
            {
                var depth = 0;
                var parent = ReadContaining(property.DeclaringType, root, path, ref depth);

                if (parent is not null
                    && property
                        .GetGetter()
                        .GetClrValue(parent) is null)
                {
                    // WHY: A raw-null or replaced optional reference can hide native nested entries.
                    // Existing original/live containers are temporary access, never the final CLR preimage.
                    NestedSetComplexEntryAccess
                        .Setter(property)
                        .SetClrValueUsingContainingEntity(root, path, instance);

                    (_staging.References ??= []).Add(property);
                }
            }
        }

        /// <summary>Reinstalls registered identities through EF's own state and positional callbacks.</summary>
        internal void Restore(
            HashSet<object> captured
        )
        {
            // WHY: Restoring a containing element can initialize new nested entries. Remove these local
            // registrations before reinstalling the captured children; never replace private native lists.
            DetachCollection(Owner, _property, new HashSet<object>(ReferenceEqualityComparer.Instance), captured);
            DetachRetained(captured);

            // WHY: Deleted/Added parents can retain opposite-lane pointers after detachment. EF's own
            // capacity method clears both lanes before rebuilding them, including their captured null holes.
            NestedSetComplexEntryAccess.Capacity(Owner, _property, 0, 0);
            NestedSetComplexEntryAccess.RestoreOriginal(Owner, _property, _original);

            // WHY: A wrapper-created element can keep an original ordinal without any original lane slot, while
            // EF validates that ordinal on every transition. RestoreCapacity trims this temporary length again.
            var current = _currentCount;
            var original = _originalCount;

            foreach (var element in Elements)
            {
                current = Math.Max(current, element.CapturedOrdinal + 1);
                original = Math.Max(original, element.CapturedOriginalOrdinal + 1);
            }

            NestedSetComplexEntryAccess.Capacity(Owner, _property, current, original);

            foreach (var element in Elements)
            {
                HideNestedOriginals(element.Native);
                element.Restore();
                element.BorrowAgain();
            }
        }

        /// <summary>Hides this frame's original so a containing transition creates no original-lane entries.</summary>
        internal void HideOriginal() => NestedSetComplexEntryAccess.RestoreOriginal(Owner, _property, null);

        /// <summary>Returns elements held as Deleted to their captured state.</summary>
        /// <returns>Whether any element was held.</returns>
        internal bool ReleaseHeldOwners()
        {
            var released = false;

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var element in Elements)
            {
                released |= element.ReleaseHold();
            }

            return released;
        }

        /// <summary>Repairs elements whose state a returning owner cascaded over.</summary>
        internal void SettleStates()
        {
            foreach (var element in Elements)
            {
                element.SettleState();
            }
        }

        /// <summary>Returns borrowed ordinals once every nested transition has settled.</summary>
        internal void ReleaseAddresses()
        {
            foreach (var element in Elements)
            {
                element.Release();
            }
        }

        /// <summary>Prevents EF from creating original-lane children during a containing element transition.</summary>
        private void HideNestedOriginals(
            object native
        )
        {
            // WHY: EF fills empty original slots with new Deleted entries that have no original buffer. Their
            // own nested collection reads then recurse between ReadPropertyValue and GetOriginalValue until the
            // stack overflows. Each nested frame reinstalls its captured original and native children itself.
            foreach (var nested in _property.ComplexType.GetFlattenedComplexProperties())
            {
                if (nested.IsCollection)
                {
                    NestedSetComplexEntryAccess.RestoreOriginal(native, nested, null);
                }
            }
        }

        /// <summary>Retires captured identities even when callbacks removed them from both cached lanes.</summary>
        internal void DetachRetained(
            HashSet<object> captured
        )
        {
            var current = Math.Max(
                _currentCount,
                NestedSetComplexEntryAccess.Entries(Owner, _property, false)
                    .Count);

            var original = Math.Max(
                _originalCount,
                NestedSetComplexEntryAccess.Entries(Owner, _property, true)
                    .Count);

            foreach (var element in Elements)
            {
                current = Math.Max(current, NestedSetComplexEntryAccess.Ordinal(element.Native, false) + 1);
                original = Math.Max(original, NestedSetComplexEntryAccess.Ordinal(element.Native, true) + 1);
            }

            NestedSetComplexEntryAccess.Capacity(Owner, _property, current, original);

            foreach (var element in Elements)
            {
                // WHY: Detection or acceptance can remove an identity from both lanes while leaving it
                // tracked. Its real Detached transition must precede ordinal changes and parent reactivation.
                DetachCollections(
                    element.Native,
                    _property.ComplexType,
                    new HashSet<object>(ReferenceEqualityComparer.Instance),
                    captured);

                NestedSetComplexEntryAccess.SetState(element.Native, EntityState.Detached);
            }
        }

        /// <summary>Restores only this collection's independent aggregate modification intent.</summary>
        internal void RestoreFlag() => NestedSetComplexEntryAccess.SetModified(Owner, _property, _modified);

        /// <summary>Checks registered identity, both native ordinals, states and the aggregate flag.</summary>
        internal void Verify()
        {
            var current = NestedSetComplexEntryAccess.Entries(Owner, _property, false);
            var original = NestedSetComplexEntryAccess.Entries(Owner, _property, true);

            if (current.Count != _currentCount
                || original.Count != _originalCount
                || CountEntries(current) != _currentElements
                || CountEntries(original) != _originalElements)
            {
                throw new InvalidOperationException(
                    "The coordinated save could not restore complex collection lane lengths and null slots.");
            }

            if (NestedSetComplexEntryAccess.Collection(Owner, _property)
                    .IsModified
                != _modified)
            {
                throw new InvalidOperationException(
                    "The coordinated save could not restore a complex collection flag.");
            }

            foreach (var element in Elements)
            {
                element.Verify(Owner, _property);
            }

            if (_hiddenProperties is not null)
            {
                foreach (var property in _hiddenProperties)
                {
                    property.Verify();
                }
            }

            foreach (var scalar in _rawScalars ?? [])
            {
                scalar.Verify();
            }
        }

        /// <summary>Allocates temporary access receipts only on the native-rebind failure path.</summary>
        private sealed class StagingSnapshot
        {
            /// <summary>The ordinal path this phase addressed, which later collection changes must not move.</summary>
            internal required int[] Path { get; init; }

            /// <summary>The CLR collection reference replaced by the temporary access list.</summary>
            internal object? Previous { get; set; }

            /// <summary>Whether the temporary collection setter needs to be reversed.</summary>
            internal bool Installed { get; set; }

            /// <summary>Original membership when temporary access grows the existing mapped list in place.</summary>
            internal object?[]? PreviousItems { get; set; }

            /// <summary>Optional references temporarily initialized above the staged collection.</summary>
            internal List<IComplexProperty>? References { get; set; }
        }
    }

    /// <summary>Retains registered elements, including original-only deletions and current additions.</summary>
    private sealed class ElementSnapshot
    {
        private readonly bool _hasCurrent;
        private readonly EntityState _state;
        private int _borrowedSlot = -1;
        private bool _heldAsDeleted;

        /// <summary>Reads native identity and positions without requiring a current CLR element.</summary>
        internal ElementSnapshot(
            object native,
            int currentSlot,
            bool hasCurrent
        )
        {
            Native = native;
            CurrentSlot = currentSlot;
            _hasCurrent = hasCurrent;
            CapturedOrdinal = NestedSetComplexEntryAccess.Ordinal(native, false);
            CapturedOriginalOrdinal = NestedSetComplexEntryAccess.Ordinal(native, true);
            _state = NestedSetComplexEntryAccess.State(native);
        }

        /// <summary>The same native entry used by all previously captured scalar wrappers.</summary>
        internal object Native { get; }

        /// <summary>The captured current ordinal, including EF's absent-lane sentinel.</summary>
        internal int CapturedOrdinal { get; }

        /// <summary>The captured original ordinal, including EF's absent-lane sentinel.</summary>
        internal int CapturedOriginalOrdinal { get; }

        /// <summary>The captured current lane position, independent of a Deleted ordinal sentinel.</summary>
        internal int CurrentSlot { get; }

        /// <summary>The original lane position, which can coexist with EF's minus-one Added sentinel.</summary>
        internal int OriginalSlot { get; set; } = -1;

        /// <summary>Whether this Deleted element also occupies a current slot.</summary>
        internal bool RetainsDeletedSlot => _state == EntityState.Deleted && CurrentSlot >= 0;

        /// <summary>Addresses a Deleted or Detached element whose ordinal sentinel has no list position.</summary>
        /// <param name="slot">A free staged slot; EF does not validate ordinals in these two states.</param>
        internal void Borrow(
            int slot
        )
        {
            _borrowedSlot = slot;
            NestedSetComplexEntryAccess.SetOrdinal(Native, slot, false);
        }

        /// <summary>Keeps a dual-lane element's staged slot, as EF clears its ordinal on becoming Deleted.</summary>
        /// <param name="slot">The captured staged slot that its descendants continue to address.</param>
        internal void KeepSlotAcrossDeletion(
            int slot
        )
        {
            if (_state == EntityState.Deleted || _heldAsDeleted)
            {
                _borrowedSlot = slot;
            }
        }

        /// <summary>Requests a temporary Deleted state while this owner's children reinstall.</summary>
        internal void HoldAsDeleted() => _heldAsDeleted = _state is EntityState.Unchanged or EntityState.Modified;

        /// <summary>Returns a held owner to its captured position and state after its children settled.</summary>
        /// <returns>Whether this element was held.</returns>
        internal bool ReleaseHold()
        {
            if (!_heldAsDeleted)
            {
                return false;
            }

            _heldAsDeleted = false;
            _borrowedSlot = -1;
            NestedSetComplexEntryAccess.SetOrdinal(Native, CapturedOrdinal, false);
            NestedSetComplexEntryAccess.SetState(Native, _state);

            return true;
        }

        /// <summary>Restores a state that EF overwrote while a containing owner returned to life.</summary>
        internal void SettleState() => NestedSetComplexEntryAccess.SetState(Native, _state);

        /// <summary>Drops any live-graph address and returns to the captured positions before reinstallation.</summary>
        internal void EndRetirement()
        {
            _borrowedSlot = -1;
            RestoreOrdinals();
        }

        /// <summary>Reapplies the borrowed slot after EF's Deleted transition reset the current ordinal.</summary>
        internal void BorrowAgain()
        {
            if (_borrowedSlot >= 0)
            {
                NestedSetComplexEntryAccess.SetOrdinal(Native, _borrowedSlot, false);
            }
        }

        /// <summary>Returns the captured ordinal after all descendants no longer need this temporary path.</summary>
        internal void Release()
        {
            if (_borrowedSlot >= 0)
            {
                _borrowedSlot = -1;
                NestedSetComplexEntryAccess.SetOrdinal(Native, CapturedOrdinal, false);
            }
        }

        /// <summary>Allows scalar flags to settle Modified/Unchanged without graph rebinding.</summary>
        internal bool MatchesLanes(
            IList current,
            IList original
        )
        {
            var state = NestedSetComplexEntryAccess.State(Native);
            var compatibleState = state == _state
                || (_hasCurrent
                    && state is EntityState.Modified or EntityState.Unchanged
                    && _state is EntityState.Modified or EntityState.Unchanged);

            return compatibleState
                && NestedSetComplexEntryAccess.Ordinal(Native, false) == CapturedOrdinal
                && NestedSetComplexEntryAccess.Ordinal(Native, true) == CapturedOriginalOrdinal
                && (CurrentSlot < 0
                    || (CurrentSlot < current.Count && ReferenceEquals(current[CurrentSlot], Native)))
                && (OriginalSlot < 0
                    || (OriginalSlot < original.Count && ReferenceEquals(original[OriginalSlot], Native)));
        }

        /// <summary>Restores positions while detached, then uses EF's normal registration notifications.</summary>
        internal void Restore()
        {
            RestoreOrdinals();

            if (HasBothLanes())
            {
                // WHY: EF retains both slots only when a tracked element transitions beneath an already
                // Added/Deleted parent. Direct Detached-to-Added/Deleted registration installs only one lane.
                NestedSetComplexEntryAccess.SetState(Native, EntityState.Unchanged);
            }

            NestedSetComplexEntryAccess.SetState(
                Native,
                _heldAsDeleted
                    ? EntityState.Deleted
                    : _state);
        }

        /// <summary>Provides captured access paths while the same native identity remains detached.</summary>
        private void RestoreOrdinals()
        {
            var dualLane = HasBothLanes();
            NestedSetComplexEntryAccess.SetOrdinal(
                Native,
                dualLane
                    ? CurrentSlot
                    : CapturedOrdinal,
                false);

            NestedSetComplexEntryAccess.SetOrdinal(
                Native,
                dualLane
                    ? OriginalSlot
                    : CapturedOriginalOrdinal,
                true);
        }

        /// <summary>Recognizes the opposite-lane pointers retained beneath Added and Deleted containers.</summary>
        private bool HasBothLanes() => (_state == EntityState.Deleted && CurrentSlot >= 0)
            || (_state == EntityState.Added && OriginalSlot >= 0);

        /// <summary>Checks lifecycle and both positional identities after all scalar flag changes.</summary>
        internal void Verify(
            object owner,
            IComplexProperty property
        )
        {
            var current = NestedSetComplexEntryAccess.Entries(owner, property, false);
            var original = NestedSetComplexEntryAccess.Entries(owner, property, true);

            if (NestedSetComplexEntryAccess.State(Native) != _state
                || NestedSetComplexEntryAccess.Ordinal(Native, false) != CapturedOrdinal
                || NestedSetComplexEntryAccess.Ordinal(Native, true) != CapturedOriginalOrdinal
                || (CurrentSlot >= 0
                    && (CurrentSlot >= current.Count || !ReferenceEquals(current[CurrentSlot], Native)))
                || (OriginalSlot >= 0
                    && (OriginalSlot >= original.Count || !ReferenceEquals(original[OriginalSlot], Native))))
            {
                throw new InvalidOperationException(
                    "The coordinated save could not restore a complex element state: "
                    + $"{property.Name}; state={NestedSetComplexEntryAccess.State(Native)}/{_state}; "
                    + $"ordinal={NestedSetComplexEntryAccess.Ordinal(Native, false)}/{CapturedOrdinal}; "
                    + $"originalOrdinal={NestedSetComplexEntryAccess.Ordinal(Native, true)}/{CapturedOriginalOrdinal}; "
                    + $"current={CurrentSlot}/{current.Count}; original={OriginalSlot}/{original.Count}.");
            }
        }
    }
}
