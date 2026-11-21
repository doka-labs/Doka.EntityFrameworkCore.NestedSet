namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    private sealed partial class CollectionSnapshot
    {
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

            // WHY: Hidden-property receipts are a stable, privately owned list during replay.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _hiddenProperties.Count; index++)
            {
                _hiddenProperties[index].Restore();
            }
        }

        /// <summary>Replays hidden CLR values after containers can have been restored over them.</summary>
        internal void RestoreHiddenValues()
        {
            if (_hiddenProperties is not null)
            {
                // WHY: Visit the existing receipt list directly; an absent list needs no empty replacement.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < _hiddenProperties.Count; index++)
                {
                    _hiddenProperties[index].RestoreClrValue();
                }
            }

            if (_rawScalars is not null)
            {
                // WHY: The captured scalar list is stable while its values are replayed.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < _rawScalars.Count; index++)
                {
                    _rawScalars[index].Restore();
                }
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
                // WHY: Verification reads each fixed hidden-property receipt without changing the list.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < _hiddenProperties.Count; index++)
                {
                    _hiddenProperties[index].Verify();
                }
            }

            if (_rawScalars is not null)
            {
                // WHY: Verification reads the fixed scalar receipt list without constructing an empty fallback.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < _rawScalars.Count; index++)
                {
                    _rawScalars[index].Verify();
                }
            }
        }
    }
}
