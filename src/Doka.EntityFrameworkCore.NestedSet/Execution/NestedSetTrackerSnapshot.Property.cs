namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetTrackerSnapshot
{
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
                    ? owner!.Property(metadata.Name)
                        .CurrentValue
                    : updateEntry!.GetCurrentValue(metadata);

            _currentValue = _comparer.Snapshot(current);
            _isModified = entry?.IsModified ?? updateEntry!.IsModified(metadata);
            _isTemporary = entry?.IsTemporary ?? updateEntry!.HasTemporaryValue(metadata);
            _hasOriginalValue = NestedSetTrackedProperty.HasOriginalValue(metadata);
            var original = _hasOriginalValue
                ? entry is null
                    ? updateEntry!.GetOriginalValue(metadata)
                    : entry.OriginalValue
                : null;

            // WHY: Identical input references can share an owned comparer snapshot. Comparer equality alone
            // is insufficient: it may consider different representations equal under a custom comparison.
            _originalValue = !_hasOriginalValue
                ? null
                : ReferenceEquals(original, current)
                    ? _currentValue
                    : _comparer.Snapshot(original);

            _instance = metadata.IsShadowProperty()
                ? null
                : instance;

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

                _clrValue = ReferenceEquals(clr, current)
                    ? _currentValue
                    : _comparer.Snapshot(clr);
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
