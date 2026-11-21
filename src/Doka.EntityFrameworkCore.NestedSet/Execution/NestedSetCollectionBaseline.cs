namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Baselines proven saved external relationships without retaining a receipt for every imported row.</summary>
internal sealed partial class NestedSetCollectionBaseline
{
    private static readonly ConditionalWeakTable<IEntityType, IForeignKey[]> s_keys = new();
    private readonly Dictionary<(IUpdateEntry Entry, INavigation Navigation), CollectionState> _originals = new();

    /// <summary>Captures preexisting input membership without copying all unrelated owner collections.</summary>
    internal static void CaptureInputOwners(
        DbContext context,
        NestedSetImportInputs inputs,
        ref NestedSetCollectionBaseline? baseline
    )
    {
        if (inputs.ForeignKeys.Count == 0)
        {
            return;
        }

        var entries = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries;

        foreach (var entry in entries)
        {
            foreach (var key in entry.EntityType.GetReferencingForeignKeys())
            {
                if (!inputs.ForeignKeys.Contains(key)
                    || key.PrincipalToDependent is not { } navigation)
                {
                    continue;
                }

                var current = entry.GetCurrentValue(navigation) as IEnumerable;
                var snapshot = entry.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot
                    ? entry.GetRelationshipSnapshotValue(navigation) as IEnumerable
                    : null;

                if (ContainsInput(current, inputs, key)
                    || ContainsInput(snapshot, inputs, key))
                {
                    (baseline ??= new NestedSetCollectionBaseline()).Capture(entry, navigation);
                }
            }
        }
    }

    /// <summary>Checks only actual references in a collection; the full input index remains lazy.</summary>
    private static bool ContainsInput(
        IEnumerable? members,
        NestedSetImportInputs inputs,
        IForeignKey key
    )
    {
        if (members is not null)
        {
            foreach (var member in members)
            {
                if (inputs.Mapping(member) is { } mapped
                    && key.DeclaringEntityType.IsAssignableFrom(mapped))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Removes known inputs from owners proven not to contain them before the operation.</summary>
    internal static void RemoveUnexpectedInputs(
        DbContext context,
        NestedSetImportInputs inputs,
        NestedSetCollectionBaseline? baseline
    )
    {
        if (inputs.ForeignKeys.Count == 0)
        {
            return;
        }

        // WHY: Collection notifications can change native entry states. Collect owners before mutating their graph.
        var owners = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .ToArray();

        foreach (var entry in owners)
        {
            foreach (var key in entry.EntityType.GetReferencingForeignKeys())
            {
                if (!inputs.ForeignKeys.Contains(key)
                    || key.PrincipalToDependent is not { } navigation
                    || baseline?._originals.ContainsKey((entry, navigation)) == true)
                {
                    continue;
                }

                var current = entry.GetCurrentValue(navigation);

                if (current is IList { IsReadOnly: false, IsFixedSize: false } list)
                {
                    // WHY: Remove only operation references while preserving unrelated rows and their positions.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var index = list.Count - 1; index >= 0; index--)
                    {
                        if (Known(list[index]))
                        {
                            list.RemoveAt(index);
                        }
                    }
                }
                else if (current is IEnumerable values)
                {
                    foreach (var member in values
                        .Cast<object>()
                        .Where(Known)
                        .ToArray())
                    {
                        navigation.GetCollectionAccessor()!.RemoveStandalone(current, member);
                    }
                }

                if (entry.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot
                    && entry.GetRelationshipSnapshotValue(navigation) is IEnumerable<object> snapshot)
                {
                    foreach (var member in snapshot
                        .Where(Known)
                        .ToArray())
                    {
                        NestedSetInsertionContract.Instance.RemoveCollectionSnapshot(entry, navigation, member);
                    }
                }

                continue;

                bool Known(
                    object? value
                ) => value is not null
                    && inputs.Mapping(value) is { } mapped
                    && key.DeclaringEntityType.IsAssignableFrom(mapped);
            }
        }
    }

    /// <summary>Captures affected tracked owners before Added fixup can change their collection snapshots.</summary>
    internal static void CaptureBeforeTracking(
        EntityEntry entry,
        ref NestedSetCollectionBaseline? baseline
    )
    {
        var keys = s_keys.GetValue(
            entry.Metadata,
            static type => type
                .GetForeignKeys()
                .Where(key => key is { IsOwnership: false, PrincipalToDependent.IsCollection: true })
                .ToArray());

        if (keys.Length == 0)
        {
            return;
        }

        var update = NestedSetInsertionTracking.EntryIdentity(entry);
        var updates = entry
            .Context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        foreach (var key in keys)
        {
            if (updates.FindPrincipal(update, key) is
                {
                    EntityState: not (EntityState.Detached or EntityState.Added),
                } principal)
            {
                (baseline ??= new NestedSetCollectionBaseline()).Capture(principal, key.PrincipalToDependent!);
            }
        }
    }

    /// <summary>Records a saved reference whose exact persisted FK resolves to an external tracked owner.</summary>
    private static void RecordSaved(
        IUpdateEntry entry,
        object entity,
        IForeignKey foreignKey,
        object?[] values,
        NestedSetTrackerSnapshot? rollback
    )
    {
        if (foreignKey.IsOwnership
            || foreignKey.PrincipalToDependent is not { IsCollection: true } navigation
            || values.Any(value => value is null))
        {
            return;
        }

        var updates = entry
            .ToEntityEntry()
            .Context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        var principal = updates.TryGetEntry(foreignKey.PrincipalKey, values);

        if (principal is null
            || principal.EntityType.GetChangeTrackingStrategy() != ChangeTrackingStrategy.Snapshot)
        {
            return;
        }

        var members = principal.GetRelationshipSnapshotValue(navigation) as ISet<object>;

        if (members?.Contains(entity) == true)
        {
            return;
        }

        var collection = principal.GetCurrentValue(navigation);

        if (collection is null
            || !navigation.GetCollectionAccessor()!.ContainsStandalone(collection, entity))
        {
            return;
        }

        // WHY: A preprepared CLR collection already contains the node, so EF's AddToCollection skipped its
        // snapshot. Baseline only this proven saved entry; unrelated untracked members remain guarded.
        // Record only our addition: the callback may already have moved other members through EF fixup.
        rollback?.RecordPersistedAddition(principal, navigation, entity);
        NestedSetInsertionContract.Instance.AddCollectionSnapshot(principal, navigation, entity);
    }

    /// <summary>Retains only the first pre-operation membership for each affected owner/navigation.</summary>
    private void Capture(
        IUpdateEntry owner,
        INavigation navigation,
        IReadOnlySet<object>? introducedEntities = null
    )
    {
        if (_originals.ContainsKey((owner, navigation)))
        {
            return;
        }

        var hasSnapshot = owner.EntityType.GetChangeTrackingStrategy() == ChangeTrackingStrategy.Snapshot;
        var values = hasSnapshot
            ? owner.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? []
            : [];

        var current = owner.GetCurrentValue(navigation);
        var members = current is IEnumerable collection
            ? collection.Cast<object>()
            : [];

        if (introducedEntities is not null)
        {
            // WHY: A callback can select another already-tracked owner. Its first snapshot may already include
            // this active batch's newly Added members; they were not part of the pre-operation native baseline.
            // Keep the captured local inside this branch: hoisting the parameter cost 24 bytes even on early return.
            var excluded = introducedEntities;
            values = values.Where(value => !excluded.Contains(value));
            members = members.Where(value => !excluded.Contains(value));
        }

        _originals.Add(
            (owner, navigation),
            new CollectionState(
                owner.ToEntityEntry()
                    .Entity,
                current,
                members.ToArray(),
                values.ToArray(),
                hasSnapshot));
    }

    /// <summary>Restores earlier membership even when an unchanged owner was absent from callback writes.</summary>
    internal void Restore()
    {
        var contract = NestedSetInsertionContract.Instance;

        foreach (var ((entry, navigation), original) in _originals)
        {
            original.RestoreCurrent(entry, navigation);

            if (!original.HasSnapshot)
            {
                // WHY: Notification collections have no relationship-snapshot slot, even after another entity
                // causes EF to allocate its relationship sidecar. CLR restoration remains independent of it.
                continue;
            }

            var current = (entry.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? []).ToArray();

            foreach (var member in current)
            {
                contract.RemoveCollectionSnapshot(entry, navigation, member);
            }

            foreach (var member in original.Snapshot)
            {
                contract.AddCollectionSnapshot(entry, navigation, member);
            }
        }
    }
}
