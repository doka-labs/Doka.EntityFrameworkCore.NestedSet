namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Preserves independent CLR graphs and installed relationships for the captured save entries.</summary>
internal sealed partial class NestedSetRelationshipSnapshot
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

    /// <summary>Restores native FK bookkeeping before raw pending CLR values are put back.</summary>
    internal void RestoreForeignKeys()
    {
        // WHY: Visit the captured FK journal by its stable insertion order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var foreignKeyIndex = 0; foreignKeyIndex < _foreignKeys.Count; foreignKeyIndex++)
        {
            var state = _foreignKeys[foreignKeyIndex];

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
        // WHY: Visit the captured FK journal by its stable insertion order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var foreignKeyIndex = 0; foreignKeyIndex < _foreignKeys.Count; foreignKeyIndex++)
        {
            var state = _foreignKeys[foreignKeyIndex];

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

        // WHY: Visit the stable pending bucket journal without changing its captured membership.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var bucketIndex = 0; bucketIndex < _pendingBuckets.Count; bucketIndex++)
        {
            var state = _pendingBuckets[bucketIndex];

            RestoreBucket(state);
        }
    }

    /// <summary>Restores installed reference/FK snapshots and collection membership after CLR restoration.</summary>
    internal void RestoreSnapshots()
    {
        var contract = NestedSetInsertionContract.Instance;

        // WHY: Visit the captured FK journal by its stable insertion order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var foreignKeyIndex = 0; foreignKeyIndex < _foreignKeys.Count; foreignKeyIndex++)
        {
            var state = _foreignKeys[foreignKeyIndex];

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

    /// <summary>Rejects an incomplete graph restoration instead of silently normalizing the caller's state.</summary>
    internal void Verify()
    {
        if (_pendingBuckets is not null)
        {
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            // WHY: Visit the stable pending bucket journal without changing its captured membership.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var bucketIndex = 0; bucketIndex < _pendingBuckets.Count; bucketIndex++)
            {
                var state = _pendingBuckets[bucketIndex];

                if (HasDependentMembership(state.Entry, state.Key, state.Values) != state.NativeMember)
                {
                    throw new InvalidOperationException("The coordinated save could not restore a pending FK bucket.");
                }
            }
        }

        // WHY: Visit the captured FK journal by its stable insertion order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var foreignKeyIndex = 0; foreignKeyIndex < _foreignKeys.Count; foreignKeyIndex++)
        {
            var state = _foreignKeys[foreignKeyIndex];

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
}
