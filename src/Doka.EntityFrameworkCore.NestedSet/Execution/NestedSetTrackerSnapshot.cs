namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>
///     Preserves pending tracked values until a coordinated or managed save has completed its transaction.
/// </summary>
internal sealed class NestedSetTrackerSnapshot
{
    private static readonly ConditionalWeakTable<IEntityType, IProperty[]> s_properties = new();
    private readonly DbContext _context;
    private readonly EntrySnapshot[] _entries;
    private readonly NestedSetRelationshipSnapshot? _relationships;
    private List<(IUpdateEntry Owner, INavigation Navigation, object Member)>? _persistedAdditions;

    /// <summary>Retains the original entry objects so rollback does not replace the caller's tracked graph.</summary>
    /// <param name="context">The context that owns the captured entries.</param>
    /// <param name="entries">The pending tracked state captured before its save sends any command.</param>
    /// <param name="relationships">The affected native relationships and caller graph, when pending FKs exist.</param>
    private NestedSetTrackerSnapshot(
        DbContext context,
        EntrySnapshot[] entries,
        NestedSetRelationshipSnapshot? relationships
    )
    {
        _context = context;
        _entries = entries;
        _relationships = relationships;
    }

    /// <summary>Captures all tracked entities after change detection and before saving without acceptance.</summary>
    /// <param name="context">The context whose hierarchy and unrelated pending changes share the save.</param>
    /// <returns>A snapshot that can restore captured values after the database transaction is rolled back.</returns>
    /// <remarks>
    ///     Complex value callbacks are journaled separately from entity membership and structural guards.
    /// </remarks>
    internal static NestedSetTrackerSnapshot Capture(
        DbContext context
    )
    {
        // WHY: The public update adapter exposes conceptual nulls that ordinary CLR values cannot represent.
        // Create() retains this context's entries; a standalone adapter would describe a different tracker.
        var updates = context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        return Capture(context, updates.Entries);
    }

    /// <summary>Captures only the pending entries that one library-owned save is about to persist.</summary>
    /// <param name="context">The context that owns the entries.</param>
    /// <param name="entries">The pending update entries passed to Entity Framework Core's persistence step.</param>
    /// <returns>A snapshot that can restore the entries after a later rollback, even when EF accepted them.</returns>
    internal static NestedSetTrackerSnapshot Capture(
        DbContext context,
        IEnumerable<IUpdateEntry> entries
    )
    {
        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            NestedSetRelationshipSnapshot? relationships = null;
            var snapshots = entries
                .Select(entry =>
                {
                    NestedSetRelationshipSnapshot.Capture(context, entry, ref relationships);

                    return new EntrySnapshot(entry);
                })
                .ToArray();

            relationships?.CaptureParentNavigations();

            return new NestedSetTrackerSnapshot(context, snapshots, relationships);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    /// <summary>Captures proven writes owned by the complete coordinated transaction.</summary>
    /// <param name="entries">The exact provider write set after application callbacks.</param>
    /// <returns>Work to install only successfully persisted collection relationships.</returns>
    internal Action? CapturePersistedRelationships(
        IList<IUpdateEntry> entries
    ) => NestedSetCollectionBaseline.CapturePersisted(entries, this);

    /// <summary>Owns only a native membership that the library is about to install after provider success.</summary>
    /// <param name="owner">The exact tracked owner resolved from a proven persisted FK.</param>
    /// <param name="navigation">The inverse collection whose native set did not contain the member.</param>
    /// <param name="member">The saved reference present in the prepared CLR collection.</param>
    internal void RecordPersistedAddition(
        IUpdateEntry owner,
        INavigation navigation,
        object member
    ) => (_persistedAdditions ??= []).Add((owner, navigation, member));

    /// <summary>
    ///     Reverses owned native additions without replaying a callback's unrelated CLR or snapshot state.
    /// </summary>
    private void UndoPersistedAdditions()
    {
        if (_persistedAdditions is null)
        {
            return;
        }

        foreach (var (owner, navigation, member) in _persistedAdditions)
        {
            // WHY: EF fixup may already have removed the member or its slot. Do not recreate an empty sidecar
            // or change any CLR collection while undoing only this library's successful native addition.
            if (owner.GetRelationshipSnapshotValue(navigation) is ISet<object> members
                && members.Contains(member))
            {
                NestedSetInsertionContract.Instance.RemoveCollectionSnapshot(owner, navigation, member);
            }
        }
    }

    /// <summary>Restores pending state after rollback without clearing or detaching the tracked graph.</summary>
    /// <exception cref="InvalidOperationException">A captured value or state could not be restored.</exception>
    internal void Restore()
    {
        var tracker = _context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;
        var cascadeDeleteTiming = tracker.CascadeDeleteTiming;
        var deleteOrphansTiming = tracker.DeleteOrphansTiming;
        tracker.AutoDetectChangesEnabled = false;
        tracker.CascadeDeleteTiming = CascadeTiming.Never;
        tracker.DeleteOrphansTiming = CascadeTiming.Never;

        try
        {
            _relationships?.PrepareRestore();

            foreach (var entry in _entries)
            {
                entry.RestoreAcceptedState();
            }

            foreach (var entry in _entries)
            {
                entry.RestoreComplexTopology();
            }

            // WHY: Restoring principal keys can update dependents; values must settle before flags are restored.
            foreach (var entry in _entries)
            {
                entry.RestoreValues();
            }

            foreach (var entry in _entries)
            {
                entry.RestoreState();
            }

            foreach (var entry in _entries)
            {
                entry.RestoreProperties();
            }

            _relationships?.RestoreForeignKeys();
            _relationships?.RestoreNavigations();

            foreach (var entry in _entries)
            {
                entry.RestoreModificationFlags();
                entry.RestoreComplexFlags();
            }

            // WHY: Temporary keys may live only in EF sidecars while the caller's CLR property still contains zero.
            foreach (var entry in _entries)
            {
                entry.RestoreClrValues();
            }

            // WHY: Deferred orphan deletion clears conceptual nulls, and earlier scalar/state writes can clear them.
            foreach (var entry in _entries)
            {
                entry.RestoreConceptualNulls();
            }

            _relationships?.RestorePendingBuckets();

            foreach (var entry in _entries)
            {
                entry.RestoreIndexerMembership();
            }

            // WHY: Authoritative pre-save snapshots win over inverse deltas, including originally present members
            // that a callback removed before the library re-added them. Late callback graph copies are never used.
            UndoPersistedAdditions();
            _relationships?.RestoreSnapshots();
            _relationships?.Verify();

            foreach (var entry in _entries)
            {
                entry.Verify();
            }

            // WHY: A successful recovery can restore an originally present member after undoing its receipt.
            // Retire only verified receipts so a repeated Restore cannot remove that original membership again.
            _persistedAdditions = null;
        }
        finally
        {
            tracker.CascadeDeleteTiming = cascadeDeleteTiming;
            tracker.DeleteOrphansTiming = deleteOrphansTiming;
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    /// <summary>Captures one entity and the scalar state of its nested complex values.</summary>
    private sealed class EntrySnapshot
    {
        private readonly EntityEntry _entry;
        private readonly EntityState _state;
        private readonly IUpdateEntry _updateEntry;
        private readonly PropertySnapshot[] _properties;
        private readonly NestedSetComplexSnapshot? _complex;

        /// <summary>Copies comparer snapshots while retaining the original entry and complex instances.</summary>
        /// <param name="entry">The existing tracked entry whose pending state must survive rollback.</param>
        internal EntrySnapshot(
            IUpdateEntry entry
        )
        {
            _updateEntry = entry;
            _entry = entry.ToEntityEntry();
            _state = entry.EntityState;
            var metadata = s_properties.GetValue(
                _entry.Metadata,
                static type => type
                    .GetProperties()
                    .ToArray());

            _properties = new PropertySnapshot[metadata.Length];

            // WHY: Fill the snapshot array directly from the corresponding metadata slots.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < metadata.Length; index++)
            {
                // WHY: Successful saves never need a PropertyEntry wrapper per scalar. Public update entries
                // expose current/original values and flags directly; rollback creates setters only when needed.
                _properties[index] = new PropertySnapshot(null, _entry, metadata[index], _entry.Entity, entry);
            }

            if (_entry
                .Metadata
                .GetComplexProperties()
                .Any())
            {
                var properties = new List<PropertySnapshot>(_properties);
                CaptureComplexValues(
                    _entry.Metadata.GetComplexProperties(),
                    _entry.ComplexProperty,
                    _entry.ComplexCollection,
                    properties,
                    [],
                    _entry.Entity,
                    hasCurrentPath: true);

                _properties = properties.ToArray();

                // WHY: Public scalar/element wrappers can initialize empty ordinal slots. Capture the lanes
                // after those reads so a raw Added CLR item retains its actual unregistered native preimage.
                _complex = new NestedSetComplexSnapshot(_entry, entry);
            }
        }

        /// <summary>
        ///     Reinstates an insertion or deletion that a successful save accepted before a later rollback.
        /// </summary>
        internal void RestoreAcceptedState()
        {
            // WHY: ChangeTracker.Clear drops identity maps but leaves entries reporting their old state, and a later
            // same-state restore would never track them again. A fresh lookup exposes that loss through public API.
            if (_entry.State != EntityState.Detached
                && _entry.Context.Entry(_entry.Entity).State == EntityState.Detached)
            {
                // WHY: Clear already zeroed EF's pending count. This transition would subtract this entry again,
                // and SaveChanges returns without writing while that count is zero.
                var contract = NestedSetInsertionContract.Instance;
                var manager = contract.Manager(_updateEntry);
                var changedCount = contract.GetChangedCount(manager);
                _entry.State = EntityState.Detached;
                contract.SetChangedCount(manager, changedCount);
            }

            // WHY: Acceptance turns additions into Unchanged and removes deletions. EF permits a store-generated key
            // to return to its temporary value only on an Added entry, so this state must precede value restoration.
            if ((_state == EntityState.Added && _entry.State == EntityState.Unchanged)
                || (_state == EntityState.Deleted && _entry.State == EntityState.Detached))
            {
                // WHY: Reattaching an accepted deletion can otherwise create fresh Deleted complex entries
                // with empty original buffers. Install the retained graph before EF cascades that transition.
                _complex?.Restore();
                _entry.State = _state;
            }
        }

        /// <summary>Restores complex containers and native positions before scalar wrappers are accessed.</summary>
        internal void RestoreComplexTopology() => _complex?.Restore();

        /// <summary>Restores independent complex collection flags after scalar state transitions.</summary>
        internal void RestoreComplexFlags() => _complex?.RestoreFlags();

        /// <summary>Restores values before state transitions can reattach additions or regenerate keys.</summary>
        internal void RestoreValues()
        {
            foreach (var property in _properties)
            {
                property.RestoreCurrentValue();
                property.RestoreOriginalValue();
            }
        }

        /// <summary>Reinstates changed entry states while preserving unchanged hidden EF information.</summary>
        internal void RestoreState()
        {
            if (_state is EntityState.Modified or EntityState.Unchanged
                && _entry.State is EntityState.Modified or EntityState.Unchanged)
            {
                RestoreModificationFlags();
            }
            else if (_entry.State != _state)
            {
                if (_complex is { } complex)
                {
                    complex.Transition(() => _entry.State = _state);
                }
                else
                {
                    _entry.State = _state;
                }
            }

            _complex?.RestoreFlags();

            if (_state == EntityState.Unchanged
                && _entry.State == EntityState.Modified)
            {
                // WHY: The whole-entity state setter rejects complex originals and replaces raw pending
                // collections. An explicit false scalar flag can finish EF's flagless sharing-peer transition
                // without rejecting that graph. False is legal on a key too; no non-key scalar is required.
                ClearUnmodifiedComplexCollections(_entry.ComplexProperties, _entry.ComplexCollections);
                _entry.Property(_entry.Metadata.FindPrimaryKey()!.Properties[0].Name).IsModified = false;
            }
        }

        /// <summary>Restores property flags after state transitions have finished modifying them.</summary>
        internal void RestoreProperties()
        {
            foreach (var property in _properties)
            {
                property.RestoreCurrentValue();
                property.RestoreOriginalValue();
                property.RestoreTemporaryFlag();

                // WHY: Setting a current or temporary value on an Added entry can also replace its original value.
                property.RestoreOriginalValue();
            }

            RestoreModificationFlags();
        }

        /// <summary>
        ///     Restores original values and modification flags after state transitions and relationship fixup.
        /// </summary>
        internal void RestoreModificationFlags()
        {
            // WHY: EF's unmodified FK setter repairs dependent maps but can replace the original value.
            // Reinstate the captured comparison baseline before restoring the caller's modification intent.
            // WHY: Clearing the last unwanted flag can change the entity to Unchanged. Desired flags therefore
            // follow in a separate pass so notification tracking reaches Modified without reading absent originals.
            foreach (var property in _properties)
            {
                property.RestoreOriginalValue();
                property.RestoreUnmodifiedFlag();
            }

            foreach (var property in _properties)
            {
                property.RestoreModifiedFlag();
            }
        }

        /// <summary>Restores the distinct mapped CLR values after temporary flags have been reinstated.</summary>
        internal void RestoreClrValues()
        {
            // WHY: A struct container holds shallow scalar references. Replaying it after comparer-owned
            // primitive collections would replace their recovered values with the callback-mutated references.
            _complex?.RestoreMembers();
            _complex?.RestoreHiddenValues();

            foreach (var property in _properties)
            {
                property.RestoreClrValue();
            }
        }

        /// <summary>Restores deferred required-relationship removals after other state changes have settled.</summary>
        internal void RestoreConceptualNulls()
        {
            foreach (var property in _properties)
            {
                property.RestoreConceptualNull();
            }
        }

        /// <summary>Removes originally absent dictionary keys after every tracker and CLR write has finished.</summary>
        internal void RestoreIndexerMembership()
        {
            foreach (var property in _properties)
            {
                property.RestoreIndexerMembership();
            }
        }

        /// <summary>Rejects incomplete restoration before the caller can unknowingly reuse changed state.</summary>
        internal void Verify()
        {
            if (_entry.State != _state)
            {
                throw new InvalidOperationException(
                    $"The coordinated save could not restore '{_entry.Metadata.DisplayName()}' from "
                    + $"'{_entry.State}' to '{_state}'.");
            }

            _complex?.Verify();

            foreach (var property in _properties)
            {
                property.Verify();
            }
        }

        /// <summary>Captures mapped scalar properties using their configured comparers and accessors.</summary>
        /// <param name="properties">The scalar entries declared on the current entity or complex type.</param>
        /// <param name="instance">The containing CLR instance, or null for a null complex reference.</param>
        /// <param name="target">The compact scalar buffer belonging to this entity snapshot.</param>
        private static void CaptureProperties(
            IEnumerable<PropertyEntry> properties,
            object? instance,
            List<PropertySnapshot> target
        )
        {
            foreach (var property in properties)
            {
                target.Add(new PropertySnapshot(property, null, property.Metadata, instance, null));
            }
        }

        /// <summary>Includes complex scalars and collection element state without treating them as entities.</summary>
        /// <param name="properties">The complex properties declared by the current structural type.</param>
        /// <param name="getReference">The public EF accessor for a complex reference.</param>
        /// <param name="getCollection">The public EF accessor for a complex collection.</param>
        /// <param name="target">The compact scalar buffer belonging to this entity snapshot.</param>
        /// <param name="ordinals">The reusable collection path, copied only for a value-type container.</param>
        /// <param name="container">The retained CLR owner or its shared value-type storage location.</param>
        /// <param name="hasCurrentPath">Whether the captured owner is reachable through current CLR storage.</param>
        private void CaptureComplexValues(
            IEnumerable<IComplexProperty> properties,
            Func<IComplexProperty, ComplexPropertyEntry> getReference,
            Func<IComplexProperty, ComplexCollectionEntry> getCollection,
            List<PropertySnapshot> target,
            List<int> ordinals,
            object? container,
            bool hasCurrentPath
        )
        {
            foreach (var property in properties)
            {
                if (property.IsCollection)
                {
                    var collection = getCollection(property);
                    var deleted = NestedSetComplexEntryAccess.State(NestedSetComplexEntryAccess.Owner(collection))
                        == EntityState.Deleted;

                    var value = deleted
                        ? NestedSetComplexEntryAccess.Original(NestedSetComplexEntryAccess.Owner(collection), property)
                        : collection.CurrentValue;

                    if (value is not IList values)
                    {
                        continue;
                    }

                    var elementComplexProperties = property.ComplexType.GetComplexProperties();

                    // WHY: EF metadata is repeatable. Probe once to avoid empty recursion, then reuse it per element
                    // without allocating a metadata array.
                    // ReSharper disable once PossibleMultipleEnumeration
                    var hasNestedProperties = elementComplexProperties.Any();

                    // WHY: Pair each complex collection value with the EF element entry at the same position.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var index = 0; index < values.Count; index++)
                    {
                        if (values[index] is null)
                        {
                            continue;
                        }

                        var element = deleted ? collection.GetOriginalEntry(index) : collection[index];
                        var instance = values[index];
                        var currentPath = hasCurrentPath && !deleted;

                        CaptureProperties(element.Properties, instance, target);

                        if (hasNestedProperties)
                        {
                            // WHY: Leaf scalars are already captured. Creating two bound member delegates
                            // for an empty recursive visit adds allocation without retaining any more state.
                            ordinals.Add(index);
                            CaptureComplexValues(
                                // ReSharper disable once PossibleMultipleEnumeration
                                elementComplexProperties,
                                element.ComplexProperty,
                                element.ComplexCollection,
                                target,
                                ordinals,
                                instance,
                                currentPath);

                            ordinals.RemoveAt(ordinals.Count - 1);
                        }
                    }

                    continue;
                }

                var reference = getReference(property);

                // WHY: A public CurrentValue beneath a Deleted owner reads an original the reference does not
                // track. The retained container is that owner's actual CLR storage in every lifecycle state.
                var owner = container is ComplexClrLocation location ? location.Captured : container;
                var referenceValue = owner is null
                    ? null
                    : property.GetGetter().GetClrValue(owner);

                var referenceLocation = referenceValue is not null && property.ComplexType.ClrType.IsValueType
                    ? new ComplexClrLocation(
                        referenceValue,
                        _entry.Entity,
                        ordinals.ToArray(),
                        hasCurrentPath,
                        container,
                        property)
                    : referenceValue;

                CaptureProperties(reference.Properties, referenceLocation, target);
                var referenceComplexProperties = property.ComplexType.GetComplexProperties();

                // WHY: EF metadata is repeatable. The probe avoids empty recursion and bound delegates without
                // allocating an array solely to enumerate the same metadata again.
                // ReSharper disable once PossibleMultipleEnumeration
                if (referenceComplexProperties.Any())
                {
                    // WHY: A scalar-only reference needs no recursive accessor delegates or ordinal work.
                    CaptureComplexValues(
                        // ReSharper disable once PossibleMultipleEnumeration
                        referenceComplexProperties,
                        reference.ComplexProperty,
                        reference.ComplexCollection,
                        target,
                        ordinals,
                        referenceLocation,
                        hasCurrentPath);
                }
            }
        }
    }

    /// <summary>Clears provider-induced aggregate flags only inside an originally Unchanged complex graph.</summary>
    /// <param name="references">The direct complex references whose nested collections may retain flags.</param>
    /// <param name="collections">The direct complex collections belonging to the current containing entry.</param>
    private static void ClearUnmodifiedComplexCollections(
        IEnumerable<ComplexPropertyEntry> references,
        IEnumerable<ComplexCollectionEntry> collections
    )
    {
        foreach (var reference in references)
        {
            if (reference.CurrentValue is not null)
            {
                ClearUnmodifiedComplexCollections(reference.ComplexProperties, reference.ComplexCollections);
            }
        }

        foreach (var collection in collections)
        {
            if (collection.CurrentValue is IList values)
            {
                // WHY: Settle nested aggregates before their containing collection. The public aggregate
                // setter visits flattened elements; originally Modified graphs never enter this clearing path.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < values.Count; index++)
                {
                    if (values[index] is not null)
                    {
                        var element = collection[index];
                        // WHY: EF's ComplexElementEntry.ComplexProperties also enumerates collections.
                        // Filter metadata before constructing reference entries, since wrapping a collection throws.
                        ClearUnmodifiedComplexCollections(
                            element
                                .Metadata
                                .ComplexType
                                .GetComplexProperties()
                                .Where(property => !property.IsCollection)
                                .Select(element.ComplexProperty),
                            element.ComplexCollections);
                    }
                }
            }

            if (collection.IsModified)
            {
                collection.IsModified = false;
            }
        }
    }

    /// <summary>Shares one retained storage location between the scalar leaves of a complex struct.</summary>
    private sealed class ComplexClrLocation
    {
        private readonly object _root;
        private readonly int[] _ordinals;
        private readonly bool _hasCurrentPath;
        private readonly object? _container;
        private readonly IComplexProperty _property;

        /// <summary>Retains mapped current or original-only storage without creating an alternate CLR graph.</summary>
        internal ComplexClrLocation(
            object captured,
            object root,
            int[] ordinals,
            bool hasCurrentPath,
            object? container,
            IComplexProperty property
        )
        {
            Captured = captured;
            _root = root;
            _ordinals = ordinals;
            _hasCurrentPath = hasCurrentPath;
            _container = container;
            _property = property;
        }

        /// <summary>Gets the boxed preimage used while independently comparer-snapshotting scalar leaves.</summary>
        internal object Captured { get; }

        /// <summary>Reads actual restored storage rather than the stale box retained at capture time.</summary>
        internal object? Read(
            IProperty property
        ) => _hasCurrentPath
            ? property
                .GetGetter()
                .GetClrValueUsingContainingEntity(_root, _ordinals)
            : property
                .GetGetter()
                .GetClrValue(ReadContainer());

        /// <summary>Restores one scalar through mapped storage, including containing-struct copyback.</summary>
        internal void Restore(
            IProperty property,
            object? value
        )
        {
            // WHY: EF's containing-entity setter cannot be built through Nullable<T>; its compiled member
            // access targets the wrapper type. Those paths copy back through their retained containers instead.
            if (_hasCurrentPath && !HasNullableContainer)
            {
                NestedSetComplexEntryAccess
                    .Setter(property)
                    .SetClrValueUsingContainingEntity(_root, _ordinals, value);

                return;
            }

            // WHY: Deleted original-only elements can have no current ordinal. Their captured original
            // storage is independent of the current graph; copy a struct back through that storage only.
            var instance = ReadContainer();
            SetMappedClrValue(instance, property, value);
            CopyBack(instance);
        }

        /// <summary>Whether this struct or a containing struct is mapped as a nullable value type.</summary>
        internal bool HasNullableContainer =>
            Nullable.GetUnderlyingType(_property.ClrType) is not null
            || (_container is ComplexClrLocation { HasNullableContainer: true });

        /// <summary>Reads the struct reference through its retained containing mapped member.</summary>
        private object ReadContainer() => _property
            .GetGetter()
            .GetClrValue(_container is ComplexClrLocation parent ? parent.ReadContainer() : _container!)!;

        /// <summary>Copies a changed original-only box through each containing value-type boundary.</summary>
        private void CopyBack(
            object value
        )
        {
            var container = _container is ComplexClrLocation parent ? parent.ReadContainer() : _container!;
            SetMappedClrValue(container, _property, value);

            if (_container is ComplexClrLocation location)
            {
                location.CopyBack(container);
            }
        }
    }

    /// <summary>Writes a mapped CLR field, property or indexer without changing tracker state or flags.</summary>
    internal static void SetMappedClrValue(
        object instance,
        IPropertyBase metadata,
        object? value
    )
    {
        var member = metadata.GetMemberInfo(forMaterialization: false, forSet: true);

        switch (member)
        {
            case FieldInfo field:
                field.SetValue(instance, value);

                break;
            case PropertyInfo property when metadata.IsIndexerProperty():
                property.SetValue(instance, value, [metadata.Name]);

                break;
            case PropertyInfo property:
                property.SetValue(instance, value);

                break;
            default:
                throw new InvalidOperationException("The mapped CLR member cannot be restored after rollback.");
        }
    }

    /// <summary>Keeps separate tracker, original, and CLR snapshots for one mapped scalar property.</summary>
    private readonly struct PropertySnapshot
    {
        private readonly PropertyEntry? _complexEntry;
        private readonly EntityEntry? _owner;
        private readonly IProperty _metadata;
        private readonly IUpdateEntry? _updateEntry;
        private readonly ValueComparer _comparer;
        private readonly object? _currentValue;
        private readonly object? _originalValue;
        private readonly object? _clrValue;
        private readonly object? _instance;
        private readonly bool _hasOriginalValue;
        private readonly bool _isModified;
        private readonly bool _isTemporary;
        private readonly bool _isConceptualNull;
        private readonly bool _isIndexerAbsent;

        /// <summary>Copies mutable values with EF comparers and reads CLR state through mapped accessors.</summary>
        /// <param name="entry">The scalar entry for a complex leaf, absent for an ordinary entity scalar.</param>
        /// <param name="owner">The containing entity entry used only for exceptional rollback setters.</param>
        /// <param name="metadata">The model-owned scalar descriptor.</param>
        /// <param name="instance">The entity or complex instance containing this scalar property.</param>
        /// <param name="updateEntry">The owning entity's public update entry, absent for complex scalars.</param>
        internal PropertySnapshot(
            PropertyEntry? entry,
            EntityEntry? owner,
            IProperty metadata,
            object? instance,
            IUpdateEntry? updateEntry
        )
        {
            _complexEntry = entry;
            _owner = owner;
            _metadata = metadata;
            _updateEntry = updateEntry;
            _comparer = metadata.GetValueComparer();
            _isConceptualNull = updateEntry?.IsConceptualNull(metadata) == true;

            // WHY: IUpdateEntry exposes a conceptual null as null, while PropertyEntry retains the non-null FK
            // beneath that flag. Rollback must preserve both values; only this exceptional scalar needs a wrapper.
            var current = entry is not null
                ? entry.CurrentValue
                : _isConceptualNull
                    ? owner!.Property(metadata.Name).CurrentValue
                    : updateEntry!.GetCurrentValue(metadata);

            _currentValue = _comparer.Snapshot(current);
            _isModified = entry?.IsModified ?? updateEntry!.IsModified(metadata);
            _isTemporary = entry?.IsTemporary ?? updateEntry!.HasTemporaryValue(metadata);
            _hasOriginalValue = NestedSetTrackedProperty.HasOriginalValue(metadata);
            var original = _hasOriginalValue
                ? entry is null ? updateEntry!.GetOriginalValue(metadata) : entry.OriginalValue
                : null;

            // WHY: Identical input references can share an owned comparer snapshot. Comparer equality alone
            // is insufficient: it may consider different representations equal under a custom comparison.
            _originalValue = !_hasOriginalValue
                ? null
                : ReferenceEquals(original, current)
                    ? _currentValue
                    : _comparer.Snapshot(original);

            _instance = metadata.IsShadowProperty() ? null : instance;
            _clrValue = null;
            _isIndexerAbsent = metadata.IsIndexerProperty()
                && instance is IDictionary<string, object?> dictionary
                && !dictionary.ContainsKey(metadata.Name);

            if (_instance is not null)
            {
                // WHY: A complex struct is boxed for capture, but rollback must address its real storage.
                // Snapshot this box now; the shared location later reads and writes the restored CLR graph.
                var clr = _instance is ComplexClrLocation location
                    ? metadata
                        .GetGetter()
                        .GetClrValue(location.Captured)
                    : ReadClrValue();

                _clrValue = ReferenceEquals(clr, current) ? _currentValue : _comparer.Snapshot(clr);
            }
        }

        /// <summary>Creates scalar setters only on the exceptional rollback path.</summary>
        private PropertyEntry Entry => _complexEntry ?? _owner!.Property(_metadata.Name);

        /// <summary>Removes generated values while preserving unchanged conceptual-null and temporary state.</summary>
        internal void RestoreCurrentValue()
        {
            if (_isTemporary && _updateEntry is not null)
            {
                // WHY: PropertyEntry.IsTemporary copies its current value into the CLR member and only changes
                // a flag. EF's generated keys instead live in a temporary sidecar while the CLR key stays at
                // its sentinel. Rebuild that sidecar before restoring the original CLR value.
                if (!_comparer.Equals(Entry.CurrentValue, _currentValue)
                    || !Entry.IsTemporary)
                {
                    TemporaryValueSetter.Method.Invoke(
                        _updateEntry,
                        BindingFlags.DoNotWrapExceptions,
                        null,
                        [_metadata, _comparer.Snapshot(_currentValue), false],
                        null);
                }

                return;
            }

            if (!_comparer.Equals(Entry.CurrentValue, _currentValue)
                || (!_isConceptualNull && ReadConceptualNull()))
            {
                if (_instance is ComplexClrLocation { HasNullableContainer: true } location)
                {
                    // WHY: PropertyEntry would build EF's unsupported setter through Nullable<T>. A complex
                    // scalar's current value lives only in CLR storage; its flags are restored separately.
                    location.Restore(_metadata, _comparer.Snapshot(_currentValue));

                    return;
                }

                Entry.CurrentValue = _comparer.Snapshot(_currentValue);
            }
        }

        /// <summary>Restores only original values that EF tracks for the configured notification strategy.</summary>
        internal void RestoreOriginalValue()
        {
            if (_hasOriginalValue && !_comparer.Equals(Entry.OriginalValue, _originalValue))
            {
                Entry.OriginalValue = _comparer.Snapshot(_originalValue);
            }
        }

        /// <summary>Clears a modification flag that was absent from the captured state.</summary>
        internal void RestoreUnmodifiedFlag()
        {
            if (!_isModified
                && Entry.IsModified)
            {
                if (_complexEntry is not null
                    && _instance is null
                    && _hasOriginalValue
                    && !_comparer.Equals(Entry.CurrentValue, Entry.OriginalValue))
                {
                    // WHY: Clearing a leaf flag normally copies its original into CLR storage. A raw-null
                    // complex container has no such storage. Align only this original for that public reset,
                    // then restore its independent buffer without invoking another detection pass.
                    var original = _comparer.Snapshot(Entry.OriginalValue);
                    Entry.OriginalValue = _comparer.Snapshot(Entry.CurrentValue);

                    try
                    {
                        Entry.IsModified = false;
                    }
                    finally
                    {
                        NestedSetComplexOriginalValue.Restore(Entry, original);
                    }
                }
                else
                {
                    Entry.IsModified = false;
                }
            }
        }

        /// <summary>Restores a captured modification flag after reinstating its associated original value.</summary>
        internal void RestoreModifiedFlag()
        {
            if (_isModified && !Entry.IsModified)
            {
                Entry.IsModified = true;
            }
        }

        /// <summary>Restores temporary flags after the captured sidecar value has been reinstated.</summary>
        internal void RestoreTemporaryFlag()
        {
            if (Entry.IsTemporary != _isTemporary)
            {
                Entry.IsTemporary = _isTemporary;
            }
        }

        /// <summary>Reinstates a removed required relationship without changing its non-nullable CLR key.</summary>
        internal void RestoreConceptualNull()
        {
            if (_isConceptualNull && !ReadConceptualNull())
            {
                // WHY: EF records null separately for a required FK and leaves its CLR value and navigations intact.
                // Orphan timing is temporarily Never, so restoring that intent does not run another cascade here.
                Entry.CurrentValue = null;
            }
        }

        /// <summary>Restores a CLR sentinel separately from the temporary value observed through the entry.</summary>
        internal void RestoreClrValue()
        {
            if (_instance is not null
                && _metadata.IsIndexerProperty()
                && _instance is IDictionary<string, object?> dictionary)
            {
                if (!_isIndexerAbsent
                    && !NestedSetStructuralValue.Matches(_metadata, ReadClrValue(), _clrValue))
                {
                    // WHY: Typed EF setters normalize a present null to the property's CLR default.
                    // Direct mapped-key assignment preserves the caller's raw dictionary representation.
                    dictionary[_metadata.Name] = _comparer.Snapshot(_clrValue);
                }

                return;
            }

            if (_instance is null
                || _comparer.Equals(ReadClrValue(), _clrValue))
            {
                return;
            }

            // WHY: PropertyEntry.IsTemporary writes its current value to the CLR member before setting the flag.
            // The mapped member preserves field access and restores the sentinel without replacing the sidecar.
            var value = _comparer.Snapshot(_clrValue);

            if (_instance is ComplexClrLocation location)
            {
                location.Restore(_metadata, value);
            }
            else
            {
                SetMappedClrValue(_instance, _metadata, value);
            }
        }

        /// <summary>Restores absent membership only after EF fixup can no longer add the mapped key again.</summary>
        internal void RestoreIndexerMembership()
        {
            if (_isIndexerAbsent)
            {
                ((IDictionary<string, object?>)_instance!).Remove(_metadata.Name);
            }
        }

        /// <summary>Checks the public tracker contract and the independently captured CLR value.</summary>
        internal void Verify()
        {
            if (!_comparer.Equals(Entry.CurrentValue, _currentValue)
                || (_hasOriginalValue && !_comparer.Equals(Entry.OriginalValue, _originalValue))
                || Entry.IsModified != _isModified
                || Entry.IsTemporary != _isTemporary
                || ReadConceptualNull() != _isConceptualNull
                || (_metadata.IsIndexerProperty()
                    && _instance is IDictionary<string, object?> dictionary
                    && dictionary.ContainsKey(_metadata.Name) == _isIndexerAbsent)
                || (_instance is not null && !_comparer.Equals(ReadClrValue(), _clrValue)))
            {
                throw new InvalidOperationException(
                    $"The coordinated save could not restore tracked property '{_metadata.Name}'.");
            }
        }

        /// <summary>Reads the public flag distinguishing a severed required relationship from its CLR key.</summary>
        /// <returns>Whether EF considers this entity scalar conceptually null.</returns>
        private bool ReadConceptualNull() => _updateEntry?.IsConceptualNull(_metadata) == true;

        /// <summary>Reads the configured field, property, or indexer without invoking a separate CLR getter.</summary>
        /// <returns>The current value of the mapped CLR member.</returns>
        private object? ReadClrValue()
        {
            if (_instance is null)
            {
                return null;
            }

            if (_instance is ComplexClrLocation location)
            {
                return location.Read(_metadata);
            }

            if (_metadata.IsIndexerProperty()
                && _instance is IDictionary<string, object?> dictionary
                && dictionary.TryGetValue(_metadata.Name, out var value))
            {
                return value;
            }

            return _metadata
                .GetGetter()
                .GetClrValue(_instance);
        }

        /// <summary>Resolves EF's temporary sidecar setter only when a failed save needs key restoration.</summary>
        private static class TemporaryValueSetter
        {
            // WHY: EF1001 correctly prevents a static reference to this version-sensitive API. Reflection is
            // confined to rollback and cached once; the positive save path never needs this internal method.
            internal static readonly MethodInfo Method = typeof(IUpdateEntry)
                    .Assembly
                    .GetType("Microsoft.EntityFrameworkCore.ChangeTracking.Internal.InternalEntryBase")
                    ?.GetMethod("SetTemporaryValue", [typeof(IProperty), typeof(object), typeof(bool)])
                ?? throw new InvalidOperationException("EF Core does not expose the expected temporary-value setter.");
        }
    }
}
