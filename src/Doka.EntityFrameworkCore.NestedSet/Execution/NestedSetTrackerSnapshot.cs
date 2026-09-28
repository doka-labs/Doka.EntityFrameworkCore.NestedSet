namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Preserves pending tracked values until a coordinated or managed save has completed its transaction.</summary>
internal sealed class NestedSetTrackerSnapshot
{
    private static readonly ConditionalWeakTable<IEntityType, IProperty[]> s_properties = new();
    private readonly DbContext _context;
    private readonly EntrySnapshot[] _entries;

    /// <summary>Retains the original entry objects so rollback does not replace the caller's tracked graph.</summary>
    /// <param name="context">The context that owns the captured entries.</param>
    /// <param name="entries">The pending tracked state captured before its save sends any command.</param>
    private NestedSetTrackerSnapshot(
        DbContext context,
        EntrySnapshot[] entries
    )
    {
        _context = context;
        _entries = entries;
    }

    /// <summary>Captures all tracked entities after change detection and before saving without acceptance.</summary>
    /// <param name="context">The context whose hierarchy and unrelated pending changes share the save.</param>
    /// <returns>A snapshot that can restore captured values after the database transaction is rolled back.</returns>
    /// <remarks>Save callbacks must not accept changes, detach entities, or replace the tracked graph.</remarks>
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
            var snapshots = entries
                .Select(entry => new EntrySnapshot(entry))
                .ToArray();

            return new NestedSetTrackerSnapshot(context, snapshots);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
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
            foreach (var entry in _entries)
            {
                entry.RestoreAcceptedState();
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

            foreach (var entry in _entries)
            {
                entry.Verify();
            }
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
        private readonly PropertySnapshot[] _properties;
        private readonly List<ComplexStateSnapshot> _complexStates = [];

        /// <summary>Copies comparer snapshots while retaining the original entry and complex instances.</summary>
        /// <param name="entry">The existing tracked entry whose pending state must survive rollback.</param>
        internal EntrySnapshot(
            IUpdateEntry entry
        )
        {
            _entry = entry.ToEntityEntry();
            _state = entry.EntityState;
            var metadata = s_properties.GetValue(
                _entry.Metadata,
                static type => type
                    .GetProperties()
                    .ToArray());

            _properties = new PropertySnapshot[metadata.Length];
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
                    properties);
                _properties = properties.ToArray();
            }
        }

        /// <summary>Reinstates an insertion or deletion that a successful save accepted before a later rollback.</summary>
        internal void RestoreAcceptedState()
        {
            // WHY: Acceptance turns additions into Unchanged and removes deletions. EF permits a store-generated key
            // to return to its temporary value only on an Added entry, so this state must precede value restoration.
            if ((_state == EntityState.Added && _entry.State == EntityState.Unchanged)
                || (_state == EntityState.Deleted && _entry.State == EntityState.Detached))
            {
                _entry.State = _state;
            }
        }

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
                _entry.State = _state;
            }

            foreach (var complexState in _complexStates)
            {
                complexState.Restore();
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

        /// <summary>Restores scalar modification flags without depending on metadata enumeration order.</summary>
        private void RestoreModificationFlags()
        {
            // WHY: Clearing the last unwanted flag can change the entity to Unchanged. Desired flags therefore
            // follow in a separate pass so notification tracking reaches Modified without reading absent originals.
            foreach (var property in _properties)
            {
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

        /// <summary>Rejects incomplete restoration before the caller can unknowingly reuse changed state.</summary>
        internal void Verify()
        {
            if (_entry.State != _state)
            {
                throw new InvalidOperationException(
                    $"The coordinated save could not restore '{_entry.Metadata.DisplayName()}' from "
                    + $"'{_entry.State}' to '{_state}'.");
            }

            foreach (var complexState in _complexStates)
            {
                complexState.Verify();
            }

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
        private void CaptureComplexValues(
            IEnumerable<IComplexProperty> properties,
            Func<IComplexProperty, ComplexPropertyEntry> getReference,
            Func<IComplexProperty, ComplexCollectionEntry> getCollection,
            List<PropertySnapshot> target
        )
        {
            foreach (var property in properties)
            {
                if (property.IsCollection)
                {
                    var collection = getCollection(property);
                    if (collection.CurrentValue is not IList values)
                    {
                        continue;
                    }

                    for (var index = 0; index < values.Count; index++)
                    {
                        if (values[index] is null)
                        {
                            continue;
                        }

                        var element = collection[index];
                        _complexStates.Add(new ComplexStateSnapshot(element));
                        CaptureProperties(element.Properties, values[index], target);
                        CaptureComplexValues(
                            property.ComplexType.GetComplexProperties(),
                            element.ComplexProperty,
                            element.ComplexCollection,
                            target);
                    }

                    continue;
                }

                var reference = getReference(property);
                CaptureProperties(reference.Properties, reference.CurrentValue, target);
                CaptureComplexValues(
                    property.ComplexType.GetComplexProperties(),
                    reference.ComplexProperty,
                    reference.ComplexCollection,
                    target);
            }
        }
    }

    /// <summary>Retains collection element state that can change when its containing entity is cascaded.</summary>
    private sealed class ComplexStateSnapshot
    {
        private readonly ComplexElementEntry _entry;
        private readonly EntityState _state;

        /// <summary>Captures a complex collection element without accepting its pending scalar changes.</summary>
        /// <param name="entry">The existing complex collection entry.</param>
        internal ComplexStateSnapshot(
            ComplexElementEntry entry
        )
        {
            _entry = entry;
            _state = entry.State;
        }

        /// <summary>Restores the element state only if saving or cascading changed it.</summary>
        internal void Restore()
        {
            if (_entry.State != _state)
            {
                _entry.State = _state;
            }
        }

        /// <summary>Ensures restoring scalar flags did not change the captured collection element state.</summary>
        internal void Verify()
        {
            if (_entry.State != _state)
            {
                throw new InvalidOperationException("The coordinated save could not restore a complex element state.");
            }
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
                    ? owner!.Property(metadata.Name)
                        .CurrentValue
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

            if (_instance is not null)
            {
                var clr = metadata
                    .GetGetter()
                    .GetClrValue(_instance);

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
                Entry.IsModified = false;
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

        /// <summary>Reinstates a removed required relationship without changing its nonnullable CLR key.</summary>
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
            if (_instance is null
                || _comparer.Equals(ReadClrValue(), _clrValue))
            {
                return;
            }

            // WHY: PropertyEntry.IsTemporary writes its current value to the CLR member before setting the flag.
            // The mapped member preserves field access and restores the sentinel without replacing the sidecar.
            var member = _metadata.GetMemberInfo(forMaterialization: false, forSet: true);
            var value = _comparer.Snapshot(_clrValue);
            switch (member)
            {
                case FieldInfo field:
                    field.SetValue(_instance, value);

                    break;
                case PropertyInfo property when _metadata.IsIndexerProperty():
                    property.SetValue(_instance, value, [_metadata.Name]);

                    break;
                case PropertyInfo property:
                    property.SetValue(_instance, value);

                    break;
                default:
                    throw new InvalidOperationException("The mapped CLR member cannot be restored after rollback.");
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
