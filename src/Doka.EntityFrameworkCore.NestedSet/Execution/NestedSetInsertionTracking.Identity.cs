using System.Runtime.ExceptionServices;

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
    /// <summary>Owns exact installed identity snapshots for one introduced root or dependent.</summary>
    internal sealed class Identity
    {
        private readonly EntityEntry _entry;
        private readonly IUpdateEntry _update;
        private readonly object _manager;
        private readonly IKey[] _keys;
        private readonly object?[][] _values;
        private readonly object?[][] _clrValues;
        private readonly IForeignKey[] _foreignKeys;
        private readonly object?[]?[] _foreignKeyValues;
        private List<ICollection<IUpdateEntry>>? _buckets;
        private List<(IKey Key, object?[] Values)>? _additionalKeys;

        /// <summary>Captures only the introduced entry and its exact native identity maps.</summary>
        internal Identity(
            EntityEntry entry
        )
        {
            _entry = entry;
            _update = EntryIdentity(entry);
            _manager = StateManager(_update);
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

            _foreignKeyValues = _foreignKeys.Length == 0 ? [] : new object?[_foreignKeys.Length][];

            Refresh();
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

                var values = new object?[key.Properties.Count];
                var clrValues = new object?[key.Properties.Count];
                var mutable = false;

                // WHY: Matching ordinals keep property metadata and both snapshot vectors aligned.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var propertyIndex = 0; propertyIndex < values.Length; propertyIndex++)
                {
                    var property = key.Properties[propertyIndex];
                    var current = _update.GetCurrentValue(property);
                    var snapshot = NestedSetStructuralValue.Snapshot(property, current);
                    values[propertyIndex] = snapshot;
                    clrValues[propertyIndex] = property.IsShadowProperty()
                        ? null
                        : NestedSetStructuralValue.Snapshot(
                            property,
                            property
                                .GetGetter()
                                .GetClrValueUsingContainingEntity(_entry.Entity));

                    mutable |= current is not null
                        && !property.ClrType.IsValueType
                        && !ReferenceEquals(current, snapshot);
                }

                var map = Contract.FindMap(_manager, key);

                if (map is not null)
                {
                    var operations = Contract.Map(map.GetType());

                    if (_values[index] is { } previous)
                    {
                        if (!ReferenceEquals(operations.Find(map, values), _update))
                        {
                            // WHY: A later Tracking callback may change an earlier root before the final
                            // capture. That current representation never became an installed identity;
                            // retain the exact old snapshot and dependent buckets for rejection cleanup.
                            continue;
                        }

                        if (!SameKey(key, previous, values))
                        {
                            operations.Remove(map, previous, _update);
                        }
                    }

                    if (mutable)
                    {
                        // WHY: EF installs live mutable key objects into a hash map. A callback can mutate their
                        // stored hash without replacing the key. Install the comparer snapshot before callbacks;
                        // native operations still use the exact entry and preserve unrelated tracked identities.
                        operations.RemoveCurrent(map, _update);
                        operations.Add(map, values, _update);
                    }
                }

                _values[index] = values;
                _clrValues[index] = clrValues;
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

                if (!reader.MatchesCurrent(_update, values[index])
                    || !reader.MatchesClr(_entry.Entity, clrValues[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Restores key representations before ordinary detachment can perform relationship fixup.</summary>
        /// <remarks>Prepare every introduced root and dependent before detaching any member of its aggregate.</remarks>
        internal void PrepareDetach()
        {
            List<Exception>? errors = null;

            // WHY: SavingChanges or a later Tracking callback can trigger EF detection and install a new
            // scalar identity before the structural guard rejects it. Capture only this entry's exact current
            // map membership before restoring CLR values, so both staged and subsequently installed slots unwind.
            // Indexing keeps each key aligned with its installed identity snapshot.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var keyIndex = 0; keyIndex < _keys.Length; keyIndex++)
            {
                var key = _keys[keyIndex];

                try
                {
                    CaptureAdditionalKey(key, _values[keyIndex], relationship: false);
                    // WHY: EF can install an intermediate key through detection, then a callback can edit CLR
                    // again before throwing. Its relationship snapshot identifies that exact installed slot;
                    // the original staged and final CLR values alone cannot find it.
                    CaptureAdditionalKey(key, _values[keyIndex], relationship: true);
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }

            CaptureDependentBuckets();

            // WHY: Each key ordinal selects the same metadata, readers and installed value vectors.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var keyIndex = 0; keyIndex < _keys.Length; keyIndex++)
            {
                var properties = _keys[keyIndex].Properties;
                var shape = s_shapes.GetValue(_keys[keyIndex], CreateShape);

                // WHY: Property ordinals keep typed readers and both restoration snapshots aligned.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                {
                    var property = properties[propertyIndex];
                    var reader = shape.Readers[propertyIndex];

                    try
                    {
                        if (property.IsShadowProperty())
                        {
                            var expected = _values[keyIndex][propertyIndex];

                            if (!reader.MatchesCurrent(_update, expected))
                            {
                                // WHY: A shadow owned FK can alias its principal's mutable key. Native fixup
                                // must see the installed identity before detachment, without flagging a new
                                // application key edit or introducing an artificial entity-state transition.
                                Contract.SetProperty(
                                    _update,
                                    property,
                                    NestedSetStructuralValue.Snapshot(property, expected));
                            }
                        }
                        else
                        {
                            var expected = _entry.State == EntityState.Added
                                ? _clrValues[keyIndex][propertyIndex]
                                : _values[keyIndex][propertyIndex];

                            if (!reader.MatchesClr(_entry.Entity, expected))
                            {
                                NestedSetStructuralValue.Assign(
                                    _entry.Entity,
                                    property,
                                    NestedSetStructuralValue.Snapshot(property, expected));
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        (errors ??= []).Add(error);
                    }
                }
            }

            if (errors is not null)
            {
                throw new AggregateException("Insertion identity restoration failed. Discard the context.", errors);
            }
        }

        /// <summary>Retains an exact additional native slot when its key differs from the staged identity.</summary>
        private void CaptureAdditionalKey(
            IKey key,
            object?[] staged,
            bool relationship
        )
        {
            var differs = false;
            var shape = s_shapes.GetValue(key, CreateShape);

            // WHY: Compare typed readers against the matching staged key slots without a projection iterator.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < key.Properties.Count; index++)
            {
                var reader = shape.Readers[index];
                var matches = relationship
                    ? reader.MatchesRelationship(_update, staged[index])
                    : reader.MatchesCurrent(_update, staged[index]);

                if (!matches)
                {
                    differs = true;
                    break;
                }
            }

            if (!differs)
            {
                return;
            }

            var values = new object?[key.Properties.Count];

            // WHY: Fill native key-vector slots directly from the same property ordinals.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = NestedSetStructuralValue.Snapshot(
                    key.Properties[index],
                    ReadKeyValue(key.Properties[index], relationship));
            }

            var map = Contract.FindMap(_manager, key);

            if (map is not null
                && ReferenceEquals(
                    Contract
                        .Map(map.GetType())
                        .Find(map, values),
                    _update))
            {
                (_additionalKeys ??= []).Add((key, values));
            }
        }

        /// <summary>Reads one known key component without allocating a callback delegate inside the row loop.</summary>
        private object? ReadKeyValue(
            IProperty property,
            bool relationship
        ) => relationship ? _update.GetRelationshipSnapshotValue(property) : _update.GetCurrentValue(property);

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

                        return reader.MatchesCurrent(_update, expected)
                            && reader.MatchesClr(_entry.Entity, expectedClr);
                    }
                }
            }

            throw new InvalidOperationException(
                "The structural node key is absent from its installed EF identity metadata.");
        }

        /// <summary>Runs ordinary detachment, then removes this entry's exact surviving map memberships.</summary>
        internal void Detach()
        {
            Exception? detachError = null;

            try
            {
                NestedSetInsertionTracking.Detach(_entry);
            }
            catch (Exception error)
            {
                detachError = error;
            }

            List<Exception>? errors = null;

            if (_entry.State == EntityState.Detached)
            {
                // WHY: Remove each identity using the installed snapshot at the same key ordinal.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < _keys.Length; index++)
                {
                    try
                    {
                        var map = Contract.FindMap(_manager, _keys[index]);

                        if (map is not null)
                        {
                            Contract
                                .Map(map.GetType())
                                .Remove(map, _values[index], _update);
                        }
                    }
                    catch (Exception error)
                    {
                        (errors ??= []).Add(error);
                    }
                }

                if (_buckets is { } buckets)
                {
                    foreach (var bucket in buckets)
                    {
                        try
                        {
                            // WHY: A composite FK can share the principal's mutable CLR object. EF's ordinary
                            // key-based removal cannot find a changed hash bucket; its captured exact collection
                            // can remove this dependent directly without scanning or clearing unrelated entries.
                            bucket.Remove(_update);
                        }
                        catch (Exception error)
                        {
                            (errors ??= []).Add(error);
                        }
                    }
                }

                if (_additionalKeys is { } additionalKeys)
                {
                    foreach (var (key, values) in additionalKeys)
                    {
                        try
                        {
                            var map = Contract.FindMap(_manager, key);

                            if (map is not null)
                            {
                                Contract
                                    .Map(map.GetType())
                                    .Remove(map, values, _update);
                            }
                        }
                        catch (Exception error)
                        {
                            (errors ??= []).Add(error);
                        }
                    }
                }
            }

            if (errors is not null)
            {
                if (detachError is not null)
                {
                    errors.Insert(0, detachError);
                }

                throw new AggregateException("Insertion identity cleanup failed. Discard the context.", errors);
            }

            if (detachError is not null)
            {
                ExceptionDispatchInfo
                    .Capture(detachError)
                    .Throw();
            }
        }

        /// <summary>Retains only exact existing foreign-key buckets that contain this introduced dependent.</summary>
        private void CaptureDependentBuckets()
        {
            if (_foreignKeys.Length == 0)
            {
                return;
            }

            var primaryKey = _entry.Metadata.FindPrimaryKey();
            var map = primaryKey is null ? null : Contract.FindMap(_manager, primaryKey);

            if (map is null)
            {
                return;
            }

            var operations = Contract.Map(map.GetType());

            // WHY: Keep each foreign key aligned with its reusable dependent-value vector.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _foreignKeys.Length; index++)
            {
                var foreignKey = _foreignKeys[index];
                var dependentMap = operations.Dependents(map, foreignKey);

                if (dependentMap is null)
                {
                    continue;
                }

                // WHY: The outer array starts with absent vectors; allocate each only for an existing dependent map.
                var values = _foreignKeyValues[index] ??= new object?[foreignKey.Properties.Count];

                var absent = false;

                // WHY: Match each FK component to its existing vector slot without building a projected sequence.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var propertyIndex = 0; propertyIndex < values.Length; propertyIndex++)
                {
                    var property = foreignKey.Properties[propertyIndex];
                    var reader = s_readers.GetValue(property, CreateReader);

                    if (!reader.MatchesCurrent(_update, values[propertyIndex]))
                    {
                        // WHY: EF's native dependent lookup accepts object vectors. Only initial or changed
                        // scalar FK components cross that boundary; unchanged components reuse their boxes.
                        values[propertyIndex] = _update.GetCurrentValue(property);
                    }

                    absent |= values[propertyIndex] is null || _update.IsConceptualNull(property);
                }

                if (absent)
                {
                    // WHY: Native dependent factories omit absent optional links. Their direct key-values
                    // lookup requires a complete non-null key, so an absent parent owns no bucket to capture.
                    continue;
                }

                var entries = Contract.Dependents(dependentMap, values);

                if (entries is ICollection<IUpdateEntry> bucket
                    && bucket.Contains(_update))
                {
                    // WHY: Most roots have no dependent bucket; allocate storage only for an actual membership.
                    var buckets = _buckets ??= [];

                    if (!buckets.Contains(bucket))
                    {
                        buckets.Add(bucket);
                    }
                }
            }
        }
    }
}
