namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Preserves independent CLR graphs and installed relationships for the captured save entries.</summary>
internal sealed class NestedSetRelationshipSnapshot
{
    private static readonly ConditionalWeakTable<IEntityType, IForeignKey[]> s_keys = new();
    private readonly List<ForeignKeyState> _foreignKeys = [];
    private readonly Dictionary<(IUpdateEntry, INavigation), NavigationState> _navigations = new();
    private readonly Dictionary<IForeignKey, HashSet<object>> _parentDependents = new();
    private readonly Dictionary<IEntityType, List<ParentDomain>> _parentDomains = new();
    private readonly IUpdateAdapter _updates;
    private List<ForeignKeyState>? _pendingBuckets;

    /// <summary>Shares the current context's native entries without creating another tracker.</summary>
    private NestedSetRelationshipSnapshot(
        DbContext context
    )
    {
        _updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();
    }

    /// <summary>Captures FK endpoints before callbacks can change an initially unchanged relationship.</summary>
    internal static void Capture(
        DbContext context,
        IUpdateEntry entry,
        ref NestedSetRelationshipSnapshot? snapshot
    )
    {
        var keys = s_keys.GetValue(
            entry.EntityType,
            static type => type
                .GetForeignKeys()
                .ToArray());

        foreach (var key in keys)
        {
            var changed = entry.EntityState is EntityState.Added or EntityState.Deleted;

            // WHY: Existing pending relationships retain full graph authority. Initially unchanged ones
            // only authorize recovery of dependents whose native identity the callback actually changes.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; !changed && index < key.Properties.Count; index++)
            {
                changed = NestedSetTrackedProperty.HasChanged(entry, key.Properties[index]);
            }

            // WHY: A callback can move an unchanged FK through EF fixup, then reset its scalar without
            // notifying EF. Capture its original endpoints now: equal scalars cannot reconstruct native
            // membership, null references or list order later. Shared owner/navigation copies are deduplicated.
            (snapshot ??= new NestedSetRelationshipSnapshot(context)).Capture(entry, key, changed);
        }
    }

    /// <summary>Captures current/installed principals and raw pending reference navigation choices.</summary>
    private void Capture(
        IUpdateEntry entry,
        IForeignKey key,
        bool authoritative
    )
    {
        var values = new object?[key.Properties.Count];
        var hasOriginals = true;
        var differentOriginals = false;

        // WHY: Independent FK snapshots must survive setters that update EF's installed relationship identity.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < values.Length; index++)
        {
            var property = key.Properties[index];
            values[index] = NestedSetStructuralValue.Snapshot(property, entry.GetRelationshipSnapshotValue(property));

            if (entry.CanHaveOriginalValue(property))
            {
                // WHY: Principal lookup uses the principal key's equality, including a configured comparer.
                differentOriginals |= !key
                    .PrincipalKey
                    .Properties[index]
                    .GetKeyValueComparer()
                    .Equals(entry.GetOriginalValue(property), values[index]);
            }
            else
            {
                hasOriginals = false;
            }
        }

        _foreignKeys.Add(new ForeignKeyState(entry, key, values, HasDependentMembership(entry, key, values)));

        if (authoritative)
        {
            CapturePendingBucket(entry, key, values);
        }

        CaptureParentDomain(entry, key);
        CapturePrincipal(_updates.FindPrincipal(entry, key), key.PrincipalToDependent, authoritative);

        if (values.All(value => value is not null))
        {
            CapturePrincipal(_updates.TryGetEntry(key.PrincipalKey, values), key.PrincipalToDependent, authoritative);
        }

        // WHY: Unchanged original and installed identities resolve the same principal. Reusing the captured
        // vector avoids another original-value array, iterator and bound delegates for every unchanged FK.
        if (hasOriginals && differentOriginals)
        {
            // WHY: Automatic detection may already install the target. Parent suppression still visits the
            // persisted/original principal, whose collection can therefore be changed by failed fixup too.
            var originals = key
                .Properties
                .Select(entry.GetOriginalValue)
                .ToArray();

            if (originals.All(value => value is not null))
            {
                CapturePrincipal(
                    _updates.TryGetEntry(key.PrincipalKey, originals),
                    key.PrincipalToDependent,
                    authoritative);
            }
        }

        if (key.DependentToPrincipal is { } reference)
        {
            CaptureNavigation(entry, reference, authoritative);
            CaptureRelatedPrincipal(entry, entry.GetCurrentValue(reference), reference, authoritative);
            CaptureRelatedPrincipal(entry, entry.GetRelationshipSnapshotValue(reference), reference, authoritative);
        }
    }

    /// <summary>Indexes only editable Parent domains whose authorized dependent can appear at a wrong target.</summary>
    private void CaptureParentDomain(
        IUpdateEntry entry,
        IForeignKey key
    )
    {
        var model = NestedSetModelMapping.For(entry.EntityType.Model);

        if (key.PrincipalToDependent is not { } navigation
            || !model.TryDescriptor(entry.EntityType, out var descriptor))
        {
            return;
        }

        var parent = descriptor.Parent.Resolve(entry.EntityType);

        if (!key.Properties.Contains(parent)
            || !NestedSetTrackedProperty.HasChanged(entry, parent))
        {
            return;
        }

        if (!_parentDependents.TryGetValue(key, out var dependents))
        {
            dependents = new HashSet<object>(ReferenceEqualityComparer.Instance);
            _parentDependents.Add(key, dependents);

            foreach (var principal in key.PrincipalEntityType.GetDerivedTypesInclusive())
            {
                if (!_parentDomains.TryGetValue(principal, out var domains))
                {
                    domains = [];
                    _parentDomains.Add(principal, domains);
                }

                domains.Add(new ParentDomain(navigation, dependents));
            }
        }

        dependents.Add(
            entry.ToEntityEntry()
                .Entity);
    }

    /// <summary>Includes a tracked principal referenced by the caller's pending CLR graph.</summary>
    private void CaptureRelatedPrincipal(
        IUpdateEntry entry,
        object? entity,
        INavigation reference,
        bool authoritative
    )
    {
        if (entity is null)
        {
            return;
        }

        var contract = NestedSetInsertionContract.Instance;
        var principal = contract.FindTrackedEntry(contract.Manager(entry), entity, reference.TargetEntityType);
        CapturePrincipal(principal, reference.Inverse, authoritative);
    }

    /// <summary>Shares each affected principal navigation snapshot across all changed dependents.</summary>
    private void CapturePrincipal(
        IUpdateEntry? entry,
        INavigation? navigation,
        bool authoritative
    )
    {
        if (entry is { EntityState: not EntityState.Detached }
            && navigation is not null)
        {
            CaptureNavigation(entry, navigation, authoritative);
        }
    }

    /// <summary>Copies a navigation once, retaining the original collection instance and member order.</summary>
    private void CaptureNavigation(
        IUpdateEntry entry,
        INavigation navigation,
        bool authoritative = true
    )
    {
        if (_navigations.TryGetValue((entry, navigation), out var existing))
        {
            existing.Promote(authoritative);

            return;
        }

        _navigations.Add((entry, navigation), new NavigationState(entry, navigation, authoritative));
    }

    /// <summary>Retains an additional native bucket only for an already divergent pending FK vector.</summary>
    private void CapturePendingBucket(
        IUpdateEntry entry,
        IForeignKey key,
        object?[] installed
    )
    {
        object?[]? current = null;

        // WHY: The common unchanged vector needs no second array or journal. A lazy EF cache can index
        // pending raw current values instead of installed snapshot values, so divergent vectors need both.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < installed.Length; index++)
        {
            var property = key.Properties[index];

            // WHY: The public update interface reports null for a conceptual null, hiding the underlying
            // pending raw key. PropertyEntry exposes that independently captured current/sidecar value.
            var value = entry.IsConceptualNull(property)
                ? entry
                    .ToEntityEntry()
                    .Property(property.Name)
                    .CurrentValue
                : entry.GetCurrentValue(property);

            if (!key
                .PrincipalKey
                .Properties[index]
                .GetKeyValueComparer()
                .Equals(value, installed[index]))
            {
                current ??= (object?[])installed.Clone();
                current[index] = NestedSetStructuralValue.Snapshot(property, value);
            }
        }

        if (current is not null
            && current.All(value => value is not null))
        {
            (_pendingBuckets ??= []).Add(
                new ForeignKeyState(entry, key, current, HasDependentMembership(entry, key, current)));
        }
    }

    /// <summary>Reads EF's exact indexed bucket without interpreting the entry's current/null flags.</summary>
    private static IEnumerable<IUpdateEntry>? DependentBucket(
        IUpdateEntry entry,
        IForeignKey key,
        object?[] values
    )
    {
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var value in values)
        {
            if (value is null)
            {
                return null;
            }
        }

        var contract = NestedSetInsertionContract.Instance;

        return contract.GetOrCreateDependents(contract.Manager(entry), values, key);
    }

    /// <summary>Reads actual bucket membership without inferring it from scalar or conceptual-null state.</summary>
    private static bool HasDependentMembership(
        IUpdateEntry entry,
        IForeignKey key,
        object?[] values
    ) =>

        // WHY: EF returns its indexed HashSet (or an empty sequence). Enumerable.Contains dispatches to
        // ICollection.Contains, so capturing one dependent does not enumerate its wide owner's bucket.
        DependentBucket(entry, key, values) is { } bucket && bucket.Contains(entry);

    /// <summary>Restores only one recorded indexed occurrence without changing FK values or other buckets.</summary>
    private static void RestoreBucket(
        ForeignKeyState state
    )
    {
        var bucket = DependentBucket(state.Entry, state.Key, state.Values);
        var present = bucket is not null && bucket.Contains(state.Entry);

        if (present == state.NativeMember)
        {
            return;
        }

        // WHY: Native Add/Remove interpret current/null flags, while Update removes the snapshot bucket.
        // EF retains mutable indexed sets for captured-present keys; mutate only that explicit key's set.
        // Reject an incompatible framework representation instead of fabricating a transient shared-FK value.
        if (bucket is not ICollection<IUpdateEntry> { IsReadOnly: false } members)
        {
            // WHY: After ChangeTracker.Clear, a read builds EF's lazy map from an empty identity map, and it then
            // misses reattached entries. A missing key has not set to mutate, so EF's own map update creates it.
            if (state.NativeMember
                && state.Entry.EntityState != EntityState.Detached)
            {
                var contract = NestedSetInsertionContract.Instance;
                contract.UpdateDependentMap(contract.Manager(state.Entry), state.Entry, state.Key);

                if (HasDependentMembership(state.Entry, state.Key, state.Values))
                {
                    return;
                }
            }

            throw new InvalidOperationException("The loaded EF dependent bucket does not support exact recovery.");
        }

        if (state.NativeMember)
        {
            members.Add(state.Entry);
        }
        else
        {
            members.Remove(state.Entry);
        }
    }

    /// <summary>
    ///     Selects native changes and explicit identity writes before rollback setters hide their intent.
    /// </summary>
    internal void PrepareRestore()
    {
        Dictionary<IForeignKey, HashSet<object>>? changed = null;

        foreach (var state in _foreignKeys)
        {
            var nativeChanged = state.Entry.EntityState == EntityState.Detached
                || HasDependentMembership(state.Entry, state.Key, state.Values) != state.NativeMember;

            var marked = false;

            // WHY: Native reference fixup can change an initially null reference without moving equal FK
            // scalars or an already absent dependent bucket. Its independent snapshot is evidence too.
            if (state.Key.DependentToPrincipal is { } reference
                && _navigations[(state.Entry, reference)].HasNativeReferenceChange)
            {
                nativeChanged = true;
            }

            // WHY: One differing component selects the whole FK, including shared/composite key effects.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < state.Values.Length; index++)
            {
                var property = state.Key.Properties[index];
                var conceptualNull = state.Entry.IsConceptualNull(property);
                marked |= state.Entry.IsModified(property);

                // WHY: Conceptual nulls remove dependent-map membership without changing the scalar snapshot.
                // Inspect every component before restoring scalars, including shared/composite required FKs.
                nativeChanged |= conceptualNull
                    || !property
                        .GetKeyValueComparer()
                        .Equals(state.Entry.GetRelationshipSnapshotValue(property), state.Values[index]);
            }

            if (!nativeChanged)
            {
                if (!marked)
                {
                    continue;
                }

                var sameIdentity = true;

                // WHY: A marked EF round trip can end at its original native identity after moving list
                // positions. A raw write to a different FK must still retain its pending CLR membership.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < state.Values.Length; index++)
                {
                    var property = state.Key.Properties[index];

                    if (!property
                        .GetKeyValueComparer()
                        .Equals(state.Entry.GetCurrentValue(property), state.Values[index]))
                    {
                        sameIdentity = false;

                        break;
                    }
                }

                if (!sameIdentity)
                {
                    continue;
                }
            }

            changed ??= new Dictionary<IForeignKey, HashSet<object>>();

            if (!changed.TryGetValue(state.Key, out var dependents))
            {
                dependents = new HashSet<object>(ReferenceEqualityComparer.Instance);
                changed.Add(state.Key, dependents);
            }

            dependents.Add(
                state.Entry.ToEntityEntry()
                    .Entity);
        }

        foreach (var state in _navigations.Values)
        {
            state.PrepareRestore(changed);
        }
    }

    /// <summary>Includes preexisting affected memberships without copying every potential parent collection.</summary>
    internal void CaptureParentNavigations()
    {
        if (_parentDomains.Count == 0)
        {
            return;
        }

        foreach (var entry in _updates.Entries)
        {
            if (!_parentDomains.TryGetValue(entry.EntityType, out var domains))
            {
                continue;
            }

            foreach (var (navigation, hashSet) in domains)
            {
                // WHY: Native snapshots may legitimately retain a known stale member. Initial guard success
                // does not prove absence; preserve only parent navigations actually containing an affected ref.
                // An earlier supplemental capture must also gain this pending Parent plan's full authority.
                if (ContainsAffected(entry.GetCurrentValue(navigation), navigation.IsCollection, hashSet)
                    || ((!navigation.IsCollection
                            || entry.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot)
                        && ContainsAffected(
                            entry.GetRelationshipSnapshotValue(navigation),
                            navigation.IsCollection,
                            hashSet)))
                {
                    CaptureNavigation(entry, navigation);
                }
            }
        }
    }

    /// <summary>Checks reference membership without allocating a complete principal graph copy.</summary>
    private static bool ContainsAffected(
        object? value,
        bool collection,
        HashSet<object> dependents
    )
    {
        if (!collection)
        {
            return value is not null && dependents.Contains(value);
        }

        if (value is IEnumerable values)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var member in values)
            {
                if (member is not null
                    && dependents.Contains(member))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Restores native FK bookkeeping before raw pending CLR values are put back.</summary>
    internal void RestoreForeignKeys()
    {
        foreach (var state in _foreignKeys)
        {
            // WHY: Normal EF setters repair dependent maps as well as snapshots; writing snapshot slots alone
            // would leave a retry observing relationships installed by the failed operation.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < state.Values.Length; index++)
            {
                var property = state.Key.Properties[index];
                var value = state.Values[index];
                var comparer = property.GetKeyValueComparer();

                if (comparer.Equals(state.Entry.GetCurrentValue(property), value))
                {
                    var installed = state.Entry.GetRelationshipSnapshotValue(property);

                    if (comparer.Equals(installed, value))
                    {
                        continue;
                    }

                    // WHY: A public FK change followed by a raw reset can leave the right scalar in the wrong
                    // native dependent bucket. Traverse the installed identity before restoring the desired one
                    // so EF repairs its maps and fixup; setting an equal scalar or only its snapshot cannot do so.
                    NestedSetInsertionContract.Instance.SetProperty(
                        state.Entry,
                        property,
                        NestedSetStructuralValue.Snapshot(property, installed));
                }

                NestedSetInsertionContract.Instance.SetProperty(
                    state.Entry,
                    property,
                    NestedSetStructuralValue.Snapshot(property, value));
            }
        }

        // WHY: Scalar-equal required-FK round trips can still lose a bucket, while a preexisting conceptual
        // null can retain one. Reconcile actual preimages after all shared vectors settle, before restoring
        // independent raw CLR keys and null flags; neither flag nor scalar equality proves membership.
        foreach (var state in _foreignKeys)
        {
            RestoreBucket(state);
        }
    }

    /// <summary>Restores exact caller graph storage without detecting its pending relationship edits.</summary>
    internal void RestoreNavigations()
    {
        RestoreUnexpectedParents();

        foreach (var state in _navigations.Values)
        {
            state.RestoreCurrent();
        }
    }

    /// <summary>Restores divergent current-value buckets after exact pending CLR/sidecar/null values return.</summary>
    internal void RestorePendingBuckets()
    {
        if (_pendingBuckets is null)
        {
            return;
        }

        foreach (var state in _pendingBuckets)
        {
            RestoreBucket(state);
        }
    }

    /// <summary>Removes callback-created wrong-target links without copying every principal collection.</summary>
    private void RestoreUnexpectedParents()
    {
        if (_parentDomains.Count == 0)
        {
            return;
        }

        // WHY: Notification-based removals can change entity states and invalidate StateManager's live iterator.
        // Retain only cleanup owners first; membership is still visited once and copied only for actual removals.
        var owners = _updates
            .Entries
            .Where(entry => _parentDomains.ContainsKey(entry.EntityType))
            .ToArray();

        foreach (var entry in owners)
        {
            if (!_parentDomains.TryGetValue(entry.EntityType, out var domains))
            {
                continue;
            }

            foreach (var domain in domains)
            {
                var navigation = domain.Navigation;

                if (_navigations.TryGetValue((entry, navigation), out var captured)
                    && captured.IsAuthoritative)
                {
                    continue;
                }

                // WHY: Initial planning ruled out wrong-target Parent effects. Remove only these authorized
                // dependents from unexpected targets; unrelated members and their order remain caller-owned.
                var current = entry.GetCurrentValue(navigation);

                if (!navigation.IsCollection)
                {
                    if (current is not null
                        && domain.Dependents.Contains(current))
                    {
                        NestedSetInsertionContract
                            .Instance
                            .Setter(navigation)
                            .SetClrValueUsingContainingEntity(
                                entry.ToEntityEntry()
                                    .Entity,
                                null);
                    }

                    if (entry.GetRelationshipSnapshotValue(navigation) is { } snapshot
                        && domain.Dependents.Contains(snapshot))
                    {
                        NestedSetInsertionContract.Instance.SetRelationshipSnapshot(entry, navigation, null);
                    }

                    continue;
                }

                if (current is IList { IsReadOnly: false, IsFixedSize: false } list)
                {
                    // WHY: Reverse removal preserves all unrelated positions and avoids notification Reset events.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var index = list.Count - 1; index >= 0; index--)
                    {
                        if (list[index] is { } member
                            && domain.Dependents.Contains(member))
                        {
                            list.RemoveAt(index);
                        }
                    }
                }
                else if (current is IEnumerable values)
                {
                    foreach (var member in values
                        .Cast<object>()
                        .Where(domain.Dependents.Contains)
                        .ToArray())
                    {
                        navigation.GetCollectionAccessor()!.RemoveStandalone(current, member);
                    }
                }

                if (entry.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot
                    && entry.GetRelationshipSnapshotValue(navigation) is IEnumerable<object> original)
                {
                    foreach (var member in original
                        .Where(domain.Dependents.Contains)
                        .ToArray())
                    {
                        NestedSetInsertionContract.Instance.RemoveCollectionSnapshot(entry, navigation, member);
                    }
                }
            }
        }
    }

    /// <summary>Restores installed reference/FK snapshots and collection membership after CLR restoration.</summary>
    internal void RestoreSnapshots()
    {
        var contract = NestedSetInsertionContract.Instance;

        foreach (var state in _foreignKeys)
        {
            // WHY: FK property ordinals correspond to the independently captured installed identity.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < state.Values.Length; index++)
            {
                var property = state.Key.Properties[index];
                contract.SetRelationshipSnapshot(
                    state.Entry,
                    property,
                    NestedSetStructuralValue.Snapshot(property, state.Values[index]));
            }
        }

        Dictionary<IForeignKey, HashSet<object>>? captured = null;

        foreach (var state in _navigations.Values)
        {
            // WHY: Unmarked FK round trips can change only an inverse native set. Select those differences
            // after owned additions are undone, without consuming the independent pending CLR reorder.
            if (state.HasNativeCollectionChange)
            {
                captured ??= CapturedDependents();
                state.IncludeSnapshotChanges(captured[state.ForeignKey]);
            }

            state.RestoreSnapshot();
        }
    }

    /// <summary>Indexes the captured dependent references once for failure-only inverse membership repair.</summary>
    private Dictionary<IForeignKey, HashSet<object>> CapturedDependents()
    {
        var captured = new Dictionary<IForeignKey, HashSet<object>>();

        foreach (var state in _foreignKeys)
        {
            if (!captured.TryGetValue(state.Key, out var members))
            {
                members = new HashSet<object>(ReferenceEqualityComparer.Instance);
                captured.Add(state.Key, members);
            }

            members.Add(
                state.Entry.ToEntityEntry()
                    .Entity);
        }

        return captured;
    }

    /// <summary>Rejects an incomplete graph restoration instead of silently normalizing the caller's state.</summary>
    internal void Verify()
    {
        if (_pendingBuckets is not null)
        {
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var state in _pendingBuckets)
            {
                if (HasDependentMembership(state.Entry, state.Key, state.Values) != state.NativeMember)
                {
                    throw new InvalidOperationException("The coordinated save could not restore a pending FK bucket.");
                }
            }
        }

        foreach (var state in _foreignKeys)
        {
            if (HasDependentMembership(state.Entry, state.Key, state.Values) != state.NativeMember)
            {
                throw new InvalidOperationException("The coordinated save could not restore an FK bucket.");
            }

            // WHY: Verify installed FK storage separately from the caller's pending raw current values.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < state.Values.Length; index++)
            {
                var property = state.Key.Properties[index];

                if (!property
                    .GetKeyValueComparer()
                    .Equals(state.Entry.GetRelationshipSnapshotValue(property), state.Values[index]))
                {
                    throw new InvalidOperationException("The coordinated save could not restore an FK snapshot.");
                }
            }
        }

        foreach (var state in _navigations.Values)
        {
            state.Verify();
        }
    }

    /// <summary>Keeps native relationship values paired with their exact dependent and FK metadata.</summary>
    private sealed record ForeignKeyState(
        IUpdateEntry Entry,
        IForeignKey Key,
        object?[] Values,
        bool NativeMember
    );

    /// <summary>Shares one bounded dependent-reference set across the FK's possible tracked principal types.</summary>
    private sealed record ParentDomain(
        INavigation Navigation,
        HashSet<object> Dependents
    );

    /// <summary>Retains current graph storage independently of EF's installed navigation snapshot.</summary>
    private sealed class NavigationState
    {
        private readonly object? _current;
        private readonly object _entity;
        private readonly IUpdateEntry _entry;
        private readonly bool _hasSnapshot;
        private readonly object[] _members;
        private readonly INavigation _navigation;
        private readonly object? _snapshot;
        private readonly object[] _snapshotMembers;
        private readonly bool _unordered;
        private HashSet<object>? _affected;
        private bool _restore;
        private bool _restoreNative;
        private object? _restoredCurrent;
        private object[]? _restoredMembers;

        /// <summary>Copies mutable membership without cloning any caller-owned entity.</summary>
        internal NavigationState(
            IUpdateEntry entry,
            INavigation navigation,
            bool authoritative
        )
        {
            _entry = entry;
            _navigation = navigation;
            _entity = entry.ToEntityEntry()
                .Entity;

            _current = entry.GetCurrentValue(navigation);
            _hasSnapshot = !navigation.IsCollection
                || entry.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot;

            _snapshot = _hasSnapshot
                ? entry.GetRelationshipSnapshotValue(navigation)
                : null;

            _members = navigation.IsCollection
                ? Members(_current)
                : [];

            _snapshotMembers = navigation.IsCollection
                ? Members(_snapshot)
                : [];

            // WHY: Reference navigation values are entities; only collection storage can have set ordering semantics.
            _unordered = navigation.IsCollection
                && _current
                    ?.GetType()
                    .GetInterfaces()
                    .Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ISet<>))
                == true;

            IsAuthoritative = authoritative;
        }

        /// <summary>Gets whether an existing pending relationship owns the complete captured navigation.</summary>
        internal bool IsAuthoritative { get; private set; }

        /// <summary>Gets the exact metadata identity shared with the captured dependent index.</summary>
        internal IForeignKey ForeignKey => _navigation.ForeignKey;

        /// <summary>Gets whether a supplemental collection's native membership differs from its preimage.</summary>
        internal bool HasNativeCollectionChange
        {
            get
            {
                if (IsAuthoritative
                    || !_navigation.IsCollection
                    || !_hasSnapshot)
                {
                    return false;
                }

                if (_entry.GetRelationshipSnapshotValue(_navigation) is not ISet<object> current)
                {
                    return _snapshotMembers.Length != 0;
                }

                if (current.Count != _snapshotMembers.Length)
                {
                    return true;
                }

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var member in _snapshotMembers)
                {
                    if (!current.Contains(member))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Gets whether EF changed this reference independently of its scalar FK identity.</summary>
        internal bool HasNativeReferenceChange =>
            !_navigation.IsCollection && !ReferenceEquals(_entry.GetRelationshipSnapshotValue(_navigation), _snapshot);

        /// <summary>Keeps existing pending-relationship authority independent of endpoint capture order.</summary>
        internal void Promote(
            bool authoritative
        ) => IsAuthoritative |= authoritative;

        /// <summary>
        ///     Limits supplemental recovery to the exact dependents with native changes or identity writes.
        /// </summary>
        internal void PrepareRestore(
            Dictionary<IForeignKey, HashSet<object>>? changed
        )
        {
            _affected = null;
            _restoredMembers = null;
            _restoredCurrent = _current;
            _restore = IsAuthoritative;
            _restoreNative = false;

            if (IsAuthoritative
                || changed is null
                || !changed.TryGetValue(_navigation.ForeignKey, out var affected))
            {
                return;
            }

            _affected = affected;
            _restore = _navigation.IsOnDependent
                ? affected.Contains(_entity)
                : _navigation.IsCollection
                    ? _members.Any(affected.Contains)
                    || _snapshotMembers.Any(affected.Contains)
                    || ContainsAffected(_entry.GetCurrentValue(_navigation), true, affected)
                    : ContainsAffected(_current, false, affected)
                    || ContainsAffected(_snapshot, false, affected)
                    || ContainsAffected(_entry.GetCurrentValue(_navigation), false, affected);
        }

        /// <summary>Restores the original collection object and ordered contents, or the exact reference.</summary>
        internal void RestoreCurrent()
        {
            if (!_restore)
            {
                return;
            }

            // WHY: An unchanged owner's EF-moved member needs its original position, while unrelated raw
            // callback additions/removals remain pending. Existing changed-FK snapshots retain full authority.
            var expected = IsAuthoritative || !_navigation.IsCollection
                ? _members
                : ProjectMembers(_affected!);

            _restoredMembers = expected;

            // WHY: Supplemental recovery owns selected members, not unrelated raw additions. A formerly
            // null collection must retain the caller's new container when those pending members need it.
            _restoredCurrent = !IsAuthoritative && _navigation.IsCollection && _current is null && expected.Length > 0
                ? _entry.GetCurrentValue(_navigation)
                : _current;

            using var pause = NestedSetNotificationPause.Begin(
                _entry,
                _navigation,
                _navigation.IsCollection
                    ? _restoredCurrent
                    : null);

            if (!ReferenceEquals(_entry.GetCurrentValue(_navigation), _restoredCurrent))
            {
                NestedSetInsertionContract
                    .Instance
                    .Setter(_navigation)
                    .SetClrValueUsingContainingEntity(_entity, _restoredCurrent);
            }

            if (!_navigation.IsCollection
                || _restoredCurrent is null
                || Members(_restoredCurrent)
                    .SequenceEqual(expected, ReferenceEqualityComparer.Instance))
            {
                return;
            }

            if (_restoredCurrent is IList { IsReadOnly: false, IsFixedSize: false } list)
            {
                // WHY: Notification collections reject Reset/Clear. Individual removals preserve EF's event contract.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = list.Count - 1; index >= 0; index--)
                {
                    list.RemoveAt(index);
                }

                foreach (var member in expected)
                {
                    list.Add(member);
                }

                return;
            }

            var accessor = _navigation.GetCollectionAccessor()!;

            foreach (var member in Members(_restoredCurrent))
            {
                accessor.RemoveStandalone(_restoredCurrent, member);
            }

            foreach (var member in expected)
            {
                accessor.AddStandalone(_restoredCurrent, member);
            }
        }

        /// <summary>Extends native-only recovery without granting authority over the pending CLR sequence.</summary>
        internal void IncludeSnapshotChanges(
            HashSet<object> captured
        )
        {
            var current = _entry.GetRelationshipSnapshotValue(_navigation) as ISet<object>;
            var original = _snapshotMembers.ToHashSet(ReferenceEqualityComparer.Instance);
            HashSet<object>? selected = null;

            // WHY: Compare each deduplicated endpoint once; scanning a wide original array per dependent
            // would make native-only recovery quadratic. Newly introduced, uncaptured members remain pending.
            foreach (var member in _snapshotMembers)
            {
                if (current?.Contains(member) != true)
                {
                    SelectSnapshotMember(member, captured, ref selected);
                }
            }

            if (current is not null)
            {
                foreach (var member in current)
                {
                    if (!original.Contains(member))
                    {
                        SelectSnapshotMember(member, captured, ref selected);
                    }
                }
            }

            if (selected is not null)
            {
                _affected = selected;
                _restoreNative = true;
            }
        }

        /// <summary>Shares native selection only after a captured member has an uncovered membership change.</summary>
        private void SelectSnapshotMember(
            object member,
            HashSet<object> captured,
            ref HashSet<object>? selected
        )
        {
            if (!captured.Contains(member)
                || (_restore && _affected?.Contains(member) == true))
            {
                return;
            }

            selected ??= _affected is null
                ? new HashSet<object>(ReferenceEqualityComparer.Instance)
                : new HashSet<object>(_affected, ReferenceEqualityComparer.Instance);

            selected.Add(member);
        }

        /// <summary>Restores membership through EF's collection API instead of its scalar snapshot setter.</summary>
        internal void RestoreSnapshot()
        {
            if ((!_restore && !_restoreNative)
                || !_hasSnapshot)
            {
                return;
            }

            var contract = NestedSetInsertionContract.Instance;

            if (!_navigation.IsCollection)
            {
                contract.SetRelationshipSnapshot(_entry, _navigation, _snapshot);

                return;
            }

            foreach (var member in Members(_entry.GetRelationshipSnapshotValue(_navigation)))
            {
                if (IsAuthoritative || _affected!.Contains(member))
                {
                    contract.RemoveCollectionSnapshot(_entry, _navigation, member);
                }
            }

            foreach (var member in _snapshotMembers)
            {
                if (IsAuthoritative || _affected!.Contains(member))
                {
                    contract.AddCollectionSnapshot(_entry, _navigation, member);
                }
            }
        }

        /// <summary>Checks exact CLR references/order and installed snapshot membership.</summary>
        internal void Verify()
        {
            if (!_restore
                && !_restoreNative)
            {
                return;
            }

            var valid = !_restore || ReferenceEquals(_entry.GetCurrentValue(_navigation), _restoredCurrent);

            if (_navigation.IsCollection)
            {
                if (_restore)
                {
                    var members = Members(_restoredCurrent);
                    var expected = _restoredMembers ?? _members;
                    valid &= _unordered
                        ? members
                            .ToHashSet(ReferenceEqualityComparer.Instance)
                            .SetEquals(expected)
                        : members.SequenceEqual(expected, ReferenceEqualityComparer.Instance);
                }

                if (_hasSnapshot)
                {
                    var snapshot = Members(_entry.GetRelationshipSnapshotValue(_navigation))
                        .ToHashSet(ReferenceEqualityComparer.Instance);

                    valid &= IsAuthoritative
                        ? snapshot.SetEquals(_snapshotMembers)
                        : snapshot
                            .Where(_affected!.Contains)
                            .ToHashSet(ReferenceEqualityComparer.Instance)
                            .SetEquals(_snapshotMembers.Where(_affected.Contains));
                }
            }
            else if (_hasSnapshot)
            {
                valid &= ReferenceEquals(_entry.GetRelationshipSnapshotValue(_navigation), _snapshot);
            }

            if (!valid)
            {
                throw new InvalidOperationException("The coordinated save could not restore a navigation graph.");
            }
        }

        /// <summary>Restores selected original occurrences without reordering unrelated pending CLR members.</summary>
        private object[] ProjectMembers(
            HashSet<object> affected
        )
        {
            var current = Members(_entry.GetCurrentValue(_navigation))
                .Where(member => !affected.Contains(member))
                .ToArray();

            // WHY: Reference-index chains preserve repeated unselected anchors without scanning a wide list
            // for every restored member. Both projection and retained scratch are linear in collection width.
            var positions = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            var previous = new int[current.Length];

            // WHY: Each position links to the preceding occurrence of that same reference, including duplicates.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Length; index++)
            {
                previous[index] = positions.GetValueOrDefault(current[index], -1);
                positions[current[index]] = index;
            }

            var gap = 0;

            foreach (var member in _members)
            {
                if (!affected.Contains(member)
                    && positions.TryGetValue(member, out var position))
                {
                    gap = position + 1;
                }
            }

            var insertions = new Dictionary<int, List<object>>();
            var count = current.Length;
            var selectedGap = current.Length;

            // WHY: Original following anchors place selected occurrences before late raw additions; trailing
            // occurrences use the last surviving original anchor. Reverse chains preserve original duplicates.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = _members.Length - 1; index >= 0; index--)
            {
                var member = _members[index];

                if (!affected.Contains(member))
                {
                    if (positions.TryGetValue(member, out var position)
                        && position >= 0)
                    {
                        gap = position;
                        positions[member] = previous[position];
                    }

                    continue;
                }

                // WHY: Raw anchor reordering can conflict with original interleaving. Preserve both the
                // selected original sequence and unselected pending sequence instead of reversing either.
                gap = Math.Min(gap, selectedGap);
                selectedGap = gap;

                if (!insertions.TryGetValue(gap, out var members))
                {
                    members = [];
                    insertions.Add(gap, members);
                }

                members.Add(member);
                count++;
            }

            var result = new object[count];
            var offset = 0;

            // WHY: Emit each pending unselected member and each restored occurrence exactly once.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index <= current.Length; index++)
            {
                if (insertions.TryGetValue(index, out var members))
                {
                    // WHY: Buckets were built in reverse original order while following duplicate anchors.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var member = members.Count - 1; member >= 0; member--)
                    {
                        result[offset++] = members[member];
                    }
                }

                if (index < current.Length)
                {
                    result[offset++] = current[index];
                }
            }

            return result;
        }

        /// <summary>Copies references in their current enumeration order, including caller-owned duplicates.</summary>
        private static object[] Members(
            object? collection
        ) => collection is IEnumerable values
            ? values
                .Cast<object>()
                .ToArray()
            : [];
    }
}
