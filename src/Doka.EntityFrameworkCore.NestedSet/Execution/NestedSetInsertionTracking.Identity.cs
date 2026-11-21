namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
    /// <summary>Owns exact installed identity snapshots for one introduced root or dependent.</summary>
    internal sealed partial class Identity
    {
        private readonly object?[][] _clrValues;
        private readonly EntityEntry _entry;
        private readonly object?[]?[] _foreignKeyValues;
        private readonly IForeignKey[] _foreignKeys;
        private readonly IKey[] _keys;
        private readonly object _manager;
        private readonly object?[][] _values;
        private List<(IKey Key, object?[] Values)>? _additionalKeys;
        private List<ICollection<IUpdateEntry>>? _buckets;

        /// <summary>Captures only the introduced entry and its exact native identity maps.</summary>
        internal Identity(
            EntityEntry entry
        )
        {
            _entry = entry;
            UpdateEntry = EntryIdentity(entry);
            _manager = StateManager(UpdateEntry);
            _keys = s_keys.GetValue(
                entry.Metadata,
                static metadata => metadata
                    .GetKeys()
                    .ToArray());

            _values = new object?[_keys.Length][];
            _clrValues = new object?[_keys.Length][];
            _foreignKeys = s_foreignKeys.GetValue(
                entry.Metadata,
                static metadata => metadata
                    .GetForeignKeys()
                    .ToArray());

            _foreignKeyValues = _foreignKeys.Length == 0
                ? []
                : new object?[_foreignKeys.Length][];

            Refresh();
        }

        /// <summary>Gets the exact active native entry for one bounded insertion wave.</summary>
        internal IUpdateEntry UpdateEntry { get; }

        /// <summary>Gets whether this entry can participate in an external principal collection baseline.</summary>
        internal bool HasCollectionRelationships =>
            _foreignKeys.Any(key => key is { IsOwnership: false, PrincipalToDependent.IsCollection: true });

        /// <summary>Adds persisted relationship vectors to the current bounded wave's shared collection scan.</summary>
        internal void CollectSavedRelationships(
            NestedSetCollectionBaseline.SavedRelationships saved
        )
        {
            // WHY: Match each persisted FK vector to its exact metadata without retaining another per-row receipt.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _foreignKeys.Length; index++)
            {
                if (_foreignKeyValues[index] is { } values)
                {
                    saved.Add(UpdateEntry, _entry.Entity, _foreignKeys[index], values);
                }
            }
        }

        /// <summary>Captures generated identities before acceptance or callbacks can mutate CLR values.</summary>
        internal void Refresh()
        {
            // WHY: Indexing keeps each key paired with its EF and CLR snapshot slots.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _keys.Length; index++)
            {
                var key = _keys[index];
                var shape = s_shapes.GetValue(key, CreateShape);

                if (!shape.RequiresSnapshot
                    && _values[index] is { } existing
                    && MatchesSnapshot(shape, existing, _clrValues[index]))
                {
                    // WHY: Typed public EF reads include shadow, temporary and generated sidecars. Comparing
                    // before the native object-vector boundary reuses unchanged scalar snapshots without boxes
                    // or arrays. Mutable reference keys still need independent snapshots and native reinstallation.
                    continue;
                }

                RefreshKey(index, key);
            }

            CaptureDependentBuckets();
        }

        /// <summary>Compares current and raw CLR scalars before allocating native key representations.</summary>
        private bool MatchesSnapshot(
            KeyShape shape,
            object?[] values,
            object?[] clrValues
        )
        {
            // WHY: Indexing compares each typed reader with both snapshot slots and stops at the first mismatch.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < shape.Readers.Length; index++)
            {
                var reader = shape.Readers[index];

                if (!reader.MatchesCurrent(UpdateEntry, values[index])
                    || !reader.MatchesClr(_entry.Entity, clrValues[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Checks a structural key against its exact installed pre-callback representation.</summary>
        /// <param name="property">The configured node key whose temporary or generated value belongs to EF.</param>
        /// <returns>Whether the callback preserved the staged or provider-generated identity.</returns>
        internal bool Matches(
            IProperty property
        )
        {
            // WHY: Retain the key ordinal needed to find its matching snapshot and reader shape.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var keyIndex = 0; keyIndex < _keys.Length; keyIndex++)
            {
                var properties = _keys[keyIndex].Properties;

                // WHY: The matching property ordinal selects its exact typed reader and saved values.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                {
                    if (properties[propertyIndex] == property)
                    {
                        var reader = s_shapes
                            .GetValue(_keys[keyIndex], CreateShape)
                            .Readers[propertyIndex];

                        var expected = _values[keyIndex][propertyIndex];

                        // WHY: SaveChanges(false) publishes SavedChanges before acceptance. Generated Added
                        // entries legitimately retain their CLR sentinel while the provider key lives in a
                        // sidecar; require that exact pre-callback CLR value until acceptance copies the key out.
                        var expectedClr = _entry.State == EntityState.Added
                            ? _clrValues[keyIndex][propertyIndex]
                            : expected;

                        return reader.MatchesCurrent(UpdateEntry, expected)
                            && reader.MatchesClr(_entry.Entity, expectedClr);
                    }
                }
            }

            throw new InvalidOperationException(
                "The structural node key is absent from its installed EF identity metadata.");
        }
    }
}
