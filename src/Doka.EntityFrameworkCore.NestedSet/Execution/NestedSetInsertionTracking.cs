using System.Runtime.ExceptionServices;

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Preserves exact native identities throughout library-owned single and bounded bulk insertions.</summary>
internal static class NestedSetInsertionTracking
{
    private static readonly ConditionalWeakTable<IEntityType, IKey[]> s_keys = new();
    private static readonly ConditionalWeakTable<IEntityType, IForeignKey[]> s_foreignKeys = new();
    private static readonly ConditionalWeakTable<IProperty, ValueReader> s_readers = new();
    private static readonly ConditionalWeakTable<IKey, KeyShape> s_shapes = new();
    private static NestedSetInsertionContract Contract => NestedSetInsertionContract.Instance;

    /// <summary>Captures bounded identities and separates mutable native map keys from live CLR values.</summary>
    /// <param name="entry">The exact insertion-owned entry after its successful Added transition.</param>
    /// <returns>The bounded lifecycle handle used before callbacks and during detachment.</returns>
    internal static Identity Capture(
        EntityEntry entry
    ) => new(entry);

    /// <summary>Restores the caller's owned membership without creating detached EF references.</summary>
    /// <param name="owner">The introduced aggregate owner whose membership belonged to this insertion.</param>
    /// <param name="navigation">The exact mapped ownership navigation or its inverse.</param>
    /// <param name="entity">The original aggregate member whose reference must remain reusable after rollback.</param>
    internal static void RestoreRelationship(
        object owner,
        INavigationBase navigation,
        object entity
    )
    {
        if (navigation.IsCollection)
        {
            navigation.GetCollectionAccessor()!.Add(owner, entity, forMaterialization: false);
        }
        else
        {
            // WHY: NavigationEntry.CurrentValue can create new strong Detached reference-map entries while
            // restoring an already-detached aggregate. EF's model-owned CLR setter preserves exact member
            // access without another tracker lifecycle or context-retained application reference.
            NestedSetDetachedValueSetter.Get(navigation)!.SetClrValueUsingContainingEntity(owner, entity);
        }
    }

    /// <summary>Rejects a failed insertion's surviving native identities after exact lifecycle cleanup.</summary>
    /// <param name="entries">Only the aggregate entries introduced by this failed insertion.</param>
    internal static void VerifyFailedCleanup(
        IEnumerable<EntityEntry> entries
    )
    {
        var introduced = new HashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance);
        var maps = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var entry in entries)
        {
            var update = EntryIdentity(entry);
            introduced.Add(update);
            var manager = StateManager(update);

            foreach (var key in entry.Metadata.GetKeys())
            {
                if (Contract.FindMap(manager, key) is { } map)
                {
                    maps.Add(map);
                }
            }
        }

        var retained = false;
        List<Exception>? errors = null;

        foreach (var map in maps)
        {
            try
            {
                // WHY: Callback-owned DetectChanges can reinstall a live mutable hash key, then a second
                // in-place edit can strand it while a final detection installs another valid current slot.
                // Exact key lookups cannot prove cleanup in that case. Only failed attempts enumerate their
                // touched maps once, matching this bounded entry set without clearing or changing other rows.
                foreach (var entry in Contract
                             .Map(map.GetType())
                             .Entries(map))
                {
                    retained |= introduced.Contains(entry);
                }
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (retained)
        {
            (errors ??= []).Add(
                new InvalidOperationException(
                    "An insertion callback left a corrupted native EF identity after exact rollback cleanup. "
                    + "Repeated in-place key changes around DetectChanges can invalidate stored hash keys. "
                    + "Discard the context."));
        }

        if (errors is not null)
        {
            throw new AggregateException(
                "Insertion identity cleanup could not be verified. Discard the context.",
                errors);
        }
    }

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

            for (var keyIndex = 0; keyIndex < _keys.Length; keyIndex++)
            {
                var properties = _keys[keyIndex].Properties;
                var shape = s_shapes.GetValue(_keys[keyIndex], CreateShape);

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
            for (var keyIndex = 0; keyIndex < _keys.Length; keyIndex++)
            {
                var properties = _keys[keyIndex].Properties;

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

    /// <summary>Obtains EF's exact invariant entry identity without inspecting private storage fields.</summary>
    /// <param name="entry">A public wrapper for an introduced root or owned dependent.</param>
    /// <returns>The context-local identity shared by every wrapper for this exact mapped entry.</returns>
    internal static IUpdateEntry EntryIdentity(
        EntityEntry entry
    ) => Contract.Entry(entry);

    /// <summary>Obtains the exact context-local native manager for the introduced entry.</summary>
    private static object StateManager(
        IUpdateEntry entry
    ) => Contract.Manager(entry);

    /// <summary>Closes public typed reads once per exact model property, outside insertion row loops.</summary>
    private static ValueReader CreateReader(
        IProperty property
    ) => (ValueReader)Activator.CreateInstance(typeof(ValueReader<>).MakeGenericType(property.ClrType), property)!;

    /// <summary>Shares immutable property readers and snapshot requirements across all rows of one model key.</summary>
    private static KeyShape CreateShape(
        IKey key
    )
    {
        var readers = new ValueReader[key.Properties.Count];
        var requiresSnapshot = false;

        for (var index = 0; index < readers.Length; index++)
        {
            var property = key.Properties[index];
            readers[index] = s_readers.GetValue(property, CreateReader);
            requiresSnapshot |= !property.ClrType.IsValueType && property.ClrType != typeof(string);
        }

        return new KeyShape(readers, requiresSnapshot);
    }

    /// <summary>Stores model-scoped typed readers without allocating snapshot flags for each inserted row.</summary>
    private sealed record KeyShape(
        ValueReader[] Readers,
        bool RequiresSnapshot
    );

    /// <summary>Compares typed current and CLR values against an already allocated native snapshot.</summary>
    private abstract class ValueReader
    {
        /// <summary>Preserves conceptual nulls while comparing EF's sidecar-aware current value.</summary>
        internal abstract bool MatchesCurrent(
            IUpdateEntry entry,
            object? expected
        );

        /// <summary>Compares the separately captured CLR value, including a generated-key sentinel.</summary>
        internal abstract bool MatchesClr(
            object entity,
            object? expected
        );

        /// <summary>Compares the installed relationship identity independently of the current CLR key.</summary>
        internal abstract bool MatchesRelationship(
            IUpdateEntry entry,
            object? expected
        );
    }

    /// <summary>Keeps one mapped scalar typed until an initial or changed snapshot needs an object vector.</summary>
    /// <typeparam name="TValue">The exact mapped CLR type, including nullable and converted domain types.</typeparam>
    private sealed class ValueReader<TValue> : ValueReader
    {
        private readonly IProperty _property;
        private readonly Func<TValue, TValue, bool> _equals;
        private readonly Func<object, TValue>? _clr;
        private readonly bool _typedCurrent;
        private readonly Func<IUpdateEntry, TValue>? _relationship;

        /// <summary>Compiles the effective EF key comparer and metadata-selected CLR member.</summary>
        public ValueReader(
            IProperty property
        )
        {
            _property = property;
            var left = Expression.Parameter(typeof(TValue), "left");
            var right = Expression.Parameter(typeof(TValue), "right");
            _equals = Expression
                .Lambda<Func<TValue, TValue, bool>>(
                    property
                        .GetKeyValueComparer()
                        .ExtractEqualsBody(left, right),
                    left,
                    right)
                .Compile();

            _clr = property.IsShadowProperty() ? null : CreateClrGetter(property);
            _typedCurrent = property.IsShadowProperty()
                || property.GetMemberInfo(false, false) switch
                {
                    FieldInfo field => field.FieldType == typeof(TValue),
                    PropertyInfo scalar => scalar.PropertyType == typeof(TValue),
                    _ => false,
                };

            _relationship = _typedCurrent ? Contract.RelationshipReader<TValue>(property) : null;
        }

        /// <inheritdoc />
        internal override bool MatchesCurrent(
            IUpdateEntry entry,
            object? expected
        )
        {
            // WHY: The public generic getter deliberately differs from EF's object getter for conceptual nulls.
            // Required FK severance must remain absent instead of collapsing to default(TValue).
            if (entry.IsConceptualNull(_property))
            {
                return expected is null;
            }

            if (!_typedCurrent)
            {
                // WHY: EF's typed accessor can read a raw member sentinel while its object getter converts that
                // sentinel to default(TValue) for differing member/model types. Preserve the original exact getter
                // at this uncommon boundary; only its required box remains, without fresh key-vector arrays.
                return _property
                    .GetKeyValueComparer()
                    .Equals(entry.GetCurrentValue(_property), expected);
            }

            var current = entry.GetCurrentValue<TValue>(_property);

            return MatchesValue(current, expected);
        }

        /// <inheritdoc />
        internal override bool MatchesClr(
            object entity,
            object? expected
        )
        {
            if (_clr is null)
            {
                return true;
            }

            var current = _clr(entity);

            return MatchesValue(current, expected);
        }

        /// <inheritdoc />
        internal override bool MatchesRelationship(
            IUpdateEntry entry,
            object? expected
        )
        {
            // WHY: A callback can return the CLR key to its staged value after detection installed another
            // identity. Snapshot reads must still find that intermediate slot and must not apply conceptual nulls.
            if (_relationship is null)
            {
                // WHY: Differing member/model types retain EF's sentinel-aware public object snapshot semantics.
                return _property
                    .GetKeyValueComparer()
                    .Equals(entry.GetRelationshipSnapshotValue(_property), expected);
            }

            return MatchesValue(_relationship(entry), expected);
        }

        /// <summary>Preserves the framework's null guards before invoking custom typed equality.</summary>
        private bool MatchesValue(
            TValue current,
            object? expected
        ) => expected is null ? current is null : current is not null && _equals(current, (TValue)expected);

        /// <summary>Uses public member selection for scalars and EF's exact getter for exceptional paths.</summary>
        private static Func<object, TValue> CreateClrGetter(
            IProperty property
        )
        {
            var member = property.GetMemberInfo(forMaterialization: false, forSet: false);

            if (property.DeclaringType is not IEntityType declaring
                || !member.DeclaringType!.IsAssignableFrom(declaring.ClrType))
            {
                // WHY: EF's public CLR getter returns object. Complex containing paths, ordinals and proxy-only
                // members retain that exact framework route rather than duplicating its access semantics. Boxing
                // remains at this exceptional boundary; ordinary mapped fields, properties and indexers stay typed.
                var getter = property.GetGetter();

                return input => (TValue)getter.GetClrValueUsingContainingEntity(input)!;
            }

            var entity = Expression.Parameter(typeof(object), "entity");
            var instance = Expression.Convert(entity, declaring.ClrType);
            Expression read = member switch
            {
                FieldInfo field => Expression.Field(instance, field),
                PropertyInfo { GetMethod: { } getter } when property.IsIndexerProperty() => Expression.Call(
                    instance,
                    getter,
                    Expression.Constant(property.Name)),
                PropertyInfo scalar => Expression.Property(instance, scalar),
                _ => throw new InvalidOperationException("The mapped insertion scalar has no readable CLR member."),
            };

            if (read.Type != typeof(TValue))
            {
                // WHY: EF applies configured sentinel semantics before converting differing member/model types.
                // A direct cast or null-only conversion would change custom sentinels. Preserve its public getter;
                // boxing at this uncommon member-conversion boundary is separate from avoidable key-vector boxes.
                var getter = property.GetGetter();

                return input => (TValue)getter.GetClrValueUsingContainingEntity(input)!;
            }

            return Expression
                .Lambda<Func<object, TValue>>(read, entity)
                .Compile();
        }
    }

    /// <summary>Compares exact key snapshots without allocating callbacks inside a row loop.</summary>
    private static bool SameKey(
        IKey key,
        IReadOnlyList<object?> left,
        IReadOnlyList<object?> right
    )
    {
        for (var index = 0; index < key.Properties.Count; index++)
        {
            if (!key
                    .Properties[index]
                    .GetKeyValueComparer()
                    .Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Uses normal detachment for tracked entries and removes a never-tracked entry's reference.</summary>
    /// <param name="entry">The exact entry introduced by the current bounded insertion batch.</param>
    internal static void Detach(
        EntityEntry entry
    )
    {
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;

            return;
        }

        // WHY: Tracking or identity installation can throw before Detached becomes Added. Reassigning Detached
        // is then an EF no-op. Native cleanup removes only this entry's partial identity and reference storage,
        // without fake state transitions, lifecycle callbacks, or clearing unrelated tracker state.
        var contract = Contract;
        var updateEntry = contract.Entry(entry);
        var stateManager = contract.Manager(updateEntry);
        var updates = entry
            .Context
            .GetService<IUpdateAdapterFactory>()
            .Create();

        foreach (var key in entry.Metadata.GetKeys())
        {
            var values = key
                .Properties
                .Select(updateEntry.GetCurrentValue)
                .ToArray();

            if (ReferenceEquals(updates.TryGetEntry(key, values), updateEntry))
            {
                // WHY: A later alternate-key conflict can leave the primary identity installed. EF's native
                // removal compares entry identity, preserving the legitimate entity owning the colliding key.
                contract.StopTracking(stateManager, updateEntry, EntityState.Detached);

                break;
            }
        }

        // WHY: Tracking callbacks fail before the reference moves; identity-map failures happen after it moves
        // to Added. Both registrations belong to this exact still-Detached input and neither removal adds state.
        contract.UpdateReferences(stateManager, updateEntry, EntityState.Detached, EntityState.Added);
        contract.UpdateReferences(stateManager, updateEntry, EntityState.Detached, EntityState.Detached);
    }
}
