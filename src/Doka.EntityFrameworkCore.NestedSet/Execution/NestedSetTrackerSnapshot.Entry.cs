namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetTrackerSnapshot
{
    /// <summary>Captures one entity and the scalar state of its nested complex values.</summary>
    private sealed partial class EntrySnapshot
    {
        private readonly NestedSetComplexSnapshot? _complex;
        private readonly EntityEntry _entry;
        private readonly PropertySnapshot[] _properties;
        private readonly EntityState _state;
        private readonly IUpdateEntry _updateEntry;

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
                && _entry.Context.Entry(_entry.Entity)
                    .State
                == EntityState.Detached)
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
                _entry.Property(_entry.Metadata.FindPrimaryKey()!.Properties[0].Name)
                    .IsModified = false;
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
                    CaptureComplexCollection(property, getCollection(property), target, ordinals, hasCurrentPath);
                }
                else
                {
                    CaptureComplexReference(
                        property,
                        getReference(property),
                        target,
                        ordinals,
                        container,
                        hasCurrentPath);
                }
            }
        }
    }
}
