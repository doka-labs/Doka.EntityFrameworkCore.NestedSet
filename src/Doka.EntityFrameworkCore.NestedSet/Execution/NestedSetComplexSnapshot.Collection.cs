namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetComplexSnapshot
{
    /// <summary>Retains one native collection's current/original positional authorities and aggregate flag.</summary>
    private sealed partial class CollectionSnapshot
    {
        private readonly int _currentCount;
        private readonly int _currentElements;
        private readonly List<HiddenPropertySnapshot>? _hiddenProperties;
        private readonly object?[]? _items;
        private readonly bool _modified;
        private readonly object? _original;
        private readonly int _originalCount;
        private readonly int _originalElements;
        private readonly List<(IComplexProperty Property, object Instance)>? _originalReferences;
        private readonly int[] _path;
        private readonly IComplexProperty _property;
        private readonly List<RawScalarSnapshot>? _rawScalars;
        private List<(IComplexProperty Property, object Instance)>? _liveReferences;
        private StagingSnapshot? _staging;

        /// <summary>Copies native element identities and positions independently of mutable CLR membership.</summary>
        internal CollectionSnapshot(
            object owner,
            IComplexProperty property,
            IList? currentValues,
            int[] path
        )
        {
            Owner = owner;
            _property = property;
            _path = path;

            _originalReferences = CaptureOriginalReferences(owner, property, currentValues);

            if (currentValues is not null)
            {
                _items = new object?[currentValues.Count];
                currentValues.CopyTo(_items, 0);
            }

            _original = NestedSetComplexEntryAccess.Original(owner, property);
            _currentCount = NestedSetComplexEntryAccess.Entries(owner, property, false)
                .Count;

            _originalCount = NestedSetComplexEntryAccess.Entries(owner, property, true)
                .Count;

            _modified = NestedSetComplexEntryAccess.Collection(owner, property)
                .IsModified;

            var current = NestedSetComplexEntryAccess.Entries(owner, property, false);
            var original = NestedSetComplexEntryAccess.Entries(owner, property, true);

            (Elements, _currentElements, _originalElements) = CaptureElements(current, original);

            CaptureRawElements(current, ref _rawScalars);

            _hiddenProperties = CaptureHiddenProperties();
        }

        /// <summary>The exact native elements present in either captured lane.</summary>
        internal ElementSnapshot[] Elements { get; }

        /// <summary>The native entry whose lanes this frame restores.</summary>
        internal object Owner { get; }

        /// <summary>Whether a captured Deleted element also occupies a current slot.</summary>
        internal bool RetainsDeletedElement => Elements.Any(element => element.RetainsDeletedSlot);

        /// <summary>Journals CLR elements that EF has not registered, because no native entry retains them.</summary>
        private void CaptureRawElements(
            IList current,
            ref List<RawScalarSnapshot>? scalars
        )
        {
            if (_items is null)
            {
                return;
            }

            // WHY: Pair every raw retained item with its captured collection ordinal.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _items.Length; index++)
            {
                if (_items[index] is { } item
                    && (index >= current.Count || current[index] is null))
                {
                    RawScalarSnapshot.Capture(_property.ComplexType, item, item, scalars ??= []);
                }
            }
        }

        /// <summary>Copies proven identical native lanes without allocating a transient identity map.</summary>
        private bool TryCaptureAligned(
            IList current,
            IList original,
            out ElementSnapshot[] elements
        )
        {
            elements = [];

            if (current.Count != original.Count)
            {
                return false;
            }

            var count = 0;

            // WHY: Matching native ordinals prove uniqueness without another reference set. Divergent
            // lanes and Added/Deleted sentinels retain the general union; paired nulls remain ordinal holes.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Count; index++)
            {
                var native = current[index];

                if (!ReferenceEquals(native, original[index]))
                {
                    return false;
                }

                if (native is null)
                {
                    continue;
                }

                if (NestedSetComplexEntryAccess.Ordinal(native, false) != index
                    || NestedSetComplexEntryAccess.Ordinal(native, true) != index)
                {
                    return false;
                }

                count++;
            }

            if (count == 0)
            {
                return true;
            }

            elements = new ElementSnapshot[count];
            var target = 0;

            // WHY: Eligibility established exact size and positional uniqueness. Each retained element
            // still captures state, both ordinals and the independent presence of its current CLR instance.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < current.Count; index++)
            {
                if (current[index] is { } native)
                {
                    elements[target++] = new ElementSnapshot(native, index, CurrentItem(index) is not null)
                    {
                        OriginalSlot = index,
                    };
                }
            }

            return true;
        }

        /// <summary>Counts installed entries separately from null ordinal slots.</summary>
        private static int CountEntries(
            IList entries
        )
        {
            var count = 0;

            // WHY: Native IReadOnlyList backing stores expose IList here. Indexing avoids boxing their
            // enumerator on every graph frame; null slots still belong to the captured positional authority.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index] is not null)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
