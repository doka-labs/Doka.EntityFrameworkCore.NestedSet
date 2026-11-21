namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetRelationshipSnapshot
{
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

        // WHY: Visit the captured FK journal by its stable insertion order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var foreignKeyIndex = 0; foreignKeyIndex < _foreignKeys.Count; foreignKeyIndex++)
        {
            var state = _foreignKeys[foreignKeyIndex];

            if (!RequiresNativeRestore(state))
            {
                continue;
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

            // WHY: Parent-domain receipts stay stable while native navigation memberships are inspected.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var domainIndex = 0; domainIndex < domains.Count; domainIndex++)
            {
                var (navigation, hashSet) = domains[domainIndex];

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

            // WHY: Parent-domain receipts stay stable while callbacks change only native or CLR memberships.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < domains.Count; index++)
            {
                RestoreUnexpectedParent(entry, domains[index]);
            }
        }
    }

    /// <summary>Indexes the captured dependent references once for failure-only inverse membership repair.</summary>
    private Dictionary<IForeignKey, HashSet<object>> CapturedDependents()
    {
        var captured = new Dictionary<IForeignKey, HashSet<object>>();

        // WHY: Visit the captured FK journal by its stable insertion order.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var foreignKeyIndex = 0; foreignKeyIndex < _foreignKeys.Count; foreignKeyIndex++)
        {
            var state = _foreignKeys[foreignKeyIndex];

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
}
