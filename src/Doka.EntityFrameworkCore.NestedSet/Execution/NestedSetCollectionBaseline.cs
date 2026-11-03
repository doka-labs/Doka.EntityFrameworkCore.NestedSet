namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Baselines proven saved external relationships without retaining a receipt for every imported row.</summary>
internal sealed class NestedSetCollectionBaseline
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

    /// <summary>Captures ordinary FK writes before commands or completion callbacks can alter their values.</summary>
    /// <param name="entries">The exact validated provider write set, without another detection pass.</param>
    /// <returns>Completion work only when the exact write set contains a proven collection relationship.</returns>
    internal static Action? CapturePersisted(
        IList<IUpdateEntry> entries
    ) => CapturePersisted(entries, null);

    /// <summary>Shares the coordinated rollback owner when a proven saved baseline needs installation.</summary>
    /// <param name="entries">The exact validated provider write set.</param>
    /// <param name="rollback">The outer coordinated snapshot, absent for ordinary EF saves.</param>
    /// <returns>Completion work restricted to relationships actually written by this attempt.</returns>
    internal static Action? CapturePersisted(
        IList<IUpdateEntry> entries,
        NestedSetTrackerSnapshot? rollback
    )
    {
        List<PendingRelationship>? pending = null;

        foreach (var entry in entries)
        {
            if (entry.EntityState != EntityState.Modified
                || entry.SharedIdentityEntry is not null
                || !NestedSetModelMapping
                    .For(entry.EntityType.Model)
                    .TryDescriptor(entry.EntityType, out _))
            {
                continue;
            }

            var keys = s_keys.GetValue(
                entry.EntityType,
                static type => type
                    .GetForeignKeys()
                    .Where(key => key is { IsOwnership: false, PrincipalToDependent.IsCollection: true })
                    .ToArray());

            foreach (var key in keys)
            {
                if (!HasWrittenForeignKey(entry, key))
                {
                    continue;
                }

                if (entry
                        .EntityType
                        .GetUpdateStoredProcedureMappings()
                        .Any()
                    && !key.Properties.All(entry.IsStoreGenerated))
                {
                    // WHY: Procedures may consume only originals or compute different values. Modified flags
                    // cannot prove their stored FK; only a complete authoritative readback can supply it.
                    continue;
                }

                var values = new object?[key.Properties.Count];
                List<int>? readBack = null;
                var proven = true;

                // WHY: Untouched FK members keep their stored originals. Current unmarked payload can differ
                // in manual mode; command callbacks can also mutate written values after SQL has consumed them.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < values.Length; index++)
                {
                    var property = key.Properties[index];
                    object? value;

                    if (entry.IsStoreGenerated(property))
                    {
                        (readBack ??= []).Add(index);

                        continue;
                    }

                    if (entry.IsModified(property)
                        && property.GetAfterSaveBehavior() == PropertySaveBehavior.Save)
                    {
                        if (entry.HasTemporaryValue(property))
                        {
                            // WHY: An inserted principal propagates its generated key into this written FK.
                            // Freezing the temporary placeholder would lose the actually saved relationship.
                            (readBack ??= []).Add(index);

                            continue;
                        }

                        value = entry.GetCurrentValue(property);
                    }
                    else if (entry.CanHaveOriginalValue(property)
                             && !property
                                 .GetTableColumnMappings()
                                 .Any(mapping =>
                                     mapping.Column.PropertyMappings.Any(other => other.Property != property)))
                    {
                        value = entry.GetOriginalValue(property);
                    }
                    else
                    {
                        // WHY: Another mapped property can write this unmarked column. Its
                        // original is not store evidence; leave EF's existing baseline and strict guards intact.
                        proven = false;

                        break;
                    }

                    values[index] = NestedSetStructuralValue.Snapshot(property, value);
                }

                if (proven)
                {
                    (pending ??= []).Add(new PendingRelationship(entry, key, values, readBack?.ToArray() ?? []));
                }
            }
        }

        return pending is null ? null : () => CompletePersisted(pending, rollback);
    }

    /// <summary>Requires an actual written member without capturing the outer loop's native entry.</summary>
    /// <param name="entry">The eligible modified hierarchy entry.</param>
    /// <param name="foreignKey">The ordinary FK whose members may be written.</param>
    /// <returns>Whether a member is provider-generated or explicitly included in the update.</returns>
    private static bool HasWrittenForeignKey(
        IUpdateEntry entry,
        IForeignKey foreignKey
    )
    {
        // WHY: A capturing predicate hoisted the outer entry before its early skip, costing 32 bytes per row.
        // Index the metadata list without another predicate or interface-enumerator allocation.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < foreignKey.Properties.Count; index++)
        {
            var property = foreignKey.Properties[index];

            if (entry.IsStoreGenerated(property)
                || (entry.IsModified(property) && property.GetAfterSaveBehavior() == PropertySaveBehavior.Save))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Installs only successfully written vectors, including actual provider-generated FK values.</summary>
    private static void CompletePersisted(
        List<PendingRelationship> pending,
        NestedSetTrackerSnapshot? rollback
    )
    {
        foreach (var relationship in pending)
        {
            var proven = true;

            foreach (var index in relationship.ReadBack)
            {
                var property = relationship.ForeignKey.Properties[index];

                if (!relationship.Entry.HasStoreGeneratedValue(property))
                {
                    proven = false;

                    break;
                }

                relationship.Values[index] = NestedSetStructuralValue.Snapshot(
                    property,
                    relationship.Entry.GetCurrentValue(property));
            }

            if (proven)
            {
                RecordSaved(
                    relationship.Entry,
                    relationship.Entry.ToEntityEntry()
                        .Entity,
                    relationship.ForeignKey,
                    relationship.Values,
                    rollback);
            }
        }
    }

    /// <summary>Retains only one write set's exact FK values until the provider succeeds or throws.</summary>
    private sealed record PendingRelationship(
        IUpdateEntry Entry,
        IForeignKey ForeignKey,
        object?[] Values,
        int[] ReadBack
    );

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
                    EntityState: not (EntityState.Detached or EntityState.Added)
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

    /// <summary>Groups only one active wave's persisted references by their exact external owner/navigation.</summary>
    /// <param name="introduced">The exact native insertion set already validated by the save guard.</param>
    /// <param name="introducedEntities">The save plan's corresponding reference-identity set.</param>
    internal sealed class SavedRelationships(
        IReadOnlySet<IUpdateEntry> introduced,
        IReadOnlySet<object> introducedEntities
    )
    {
        private readonly Dictionary<(IUpdateEntry Entry, INavigation Navigation), HashSet<object>> _groups = new();
        private IUpdateAdapter? _updates;

        /// <summary>Resolves an external tracked owner from the immutable persisted FK vector.</summary>
        internal void Add(
            IUpdateEntry entry,
            object entity,
            IForeignKey foreignKey,
            object?[] values
        )
        {
            if (foreignKey.IsOwnership
                || foreignKey.PrincipalToDependent is not { IsCollection: true } navigation
                || values.Any(value => value is null))
            {
                return;
            }

            _updates ??= entry
                .ToEntityEntry()
                .Context
                .GetService<IUpdateAdapterFactory>()
                .Create();

            var principal = _updates.TryGetEntry(foreignKey.PrincipalKey, values);

            if (principal is null
                || introduced.Contains(principal)
                || principal.EntityType.GetChangeTrackingStrategy() != ChangeTrackingStrategy.Snapshot)
            {
                return;
            }

            if (!_groups.TryGetValue((principal, navigation), out var members))
            {
                members = new HashSet<object>(ReferenceEqualityComparer.Instance);
                _groups.Add((principal, navigation), members);
            }

            members.Add(entity);
        }

        /// <summary>Baselines exact persisted references with one collection scan per owner in this wave.</summary>
        internal void Record(
            ref NestedSetCollectionBaseline? baseline
        )
        {
            if (_groups.Count == 0)
            {
                return;
            }

            var journal = baseline ??= new NestedSetCollectionBaseline();

            foreach (var ((principal, navigation), saved) in _groups)
            {
                // WHY: A newly selected owner needs its rollback baseline even when EF already installed the
                // saved member. Capture before testing native membership, excluding only this active wave.
                journal.Capture(principal, navigation, introducedEntities);

                if (principal.GetCurrentValue(navigation) is not IEnumerable collection)
                {
                    continue;
                }

                var snapshot = principal.GetRelationshipSnapshotValue(navigation) as ISet<object>;

                // WHY: Prepared lists can hold the whole import. A bounded reference set scans each owner once
                // per wave instead of invoking List.Contains separately for every newly saved member.
                foreach (var member in collection)
                {
                    if (saved.Remove(member)
                        && snapshot?.Contains(member) != true)
                    {
                        NestedSetInsertionContract.Instance.AddCollectionSnapshot(principal, navigation, member);
                    }

                    if (saved.Count == 0)
                    {
                        break;
                    }
                }
            }
        }
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
        var values = hasSnapshot ? owner.GetRelationshipSnapshotValue(navigation) as IEnumerable<object> ?? [] : [];
        var current = owner.GetCurrentValue(navigation);
        var members = current is IEnumerable collection ? collection.Cast<object>() : [];

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

    /// <summary>Preserves one actual external owner graph, not an entry receipt for each imported row.</summary>
    private sealed record CollectionState(
        object Owner,
        object? Collection,
        object[] Members,
        object[] Snapshot,
        bool HasSnapshot
    )
    {
        /// <summary>Restores the caller's collection object and ordered references after input detachment.</summary>
        internal void RestoreCurrent(
            IUpdateEntry entry,
            INavigation navigation
        )
        {
            using var pause = NestedSetNotificationPause.Begin(entry, navigation, Collection);

            if (!ReferenceEquals(entry.GetCurrentValue(navigation), Collection))
            {
                NestedSetInsertionContract
                    .Instance
                    .Setter(navigation)
                    .SetClrValueUsingContainingEntity(Owner, Collection);
            }

            if (Collection is null)
            {
                return;
            }

            var current = ((IEnumerable)Collection)
                .Cast<object>()
                .ToArray();

            if (current.SequenceEqual(Members, ReferenceEqualityComparer.Instance))
            {
                return;
            }

            if (Collection is IList { IsReadOnly: false, IsFixedSize: false } list)
            {
                // WHY: Added-entry cleanup removes active inputs from a principal's CLR collection. Rebuild
                // the original order without Clear/Reset, including preprepared later-wave references.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = list.Count - 1; index >= 0; index--)
                {
                    list.RemoveAt(index);
                }

                foreach (var member in Members)
                {
                    list.Add(member);
                }
            }
            else
            {
                var accessor = navigation.GetCollectionAccessor()!;

                foreach (var member in current)
                {
                    accessor.RemoveStandalone(Collection, member);
                }

                foreach (var member in Members)
                {
                    accessor.AddStandalone(Collection, member);
                }
            }
        }
    }
}
