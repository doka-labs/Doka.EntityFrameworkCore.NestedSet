namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    /// <summary>Freezes every hierarchy/order scalar without retaining wrappers for unrelated payload.</summary>
    private sealed class CandidatePlan
    {
        private static readonly ConditionalWeakTable<IEntityType, GuardReader[]> s_readers = new();
        private readonly object _entity;
        private readonly object? _intendedParent;
        private readonly int _parentIndex;
        private readonly GuardValue[] _values;

        /// <summary>Retains independent snapshots for one original candidate.</summary>
        private CandidatePlan(
            GuardValue[] values,
            object entity,
            IProperty parent
        )
        {
            _values = values;
            _entity = entity;
            _parentIndex = Array.FindIndex(values, value => value.Reader.Property == parent && value.Modified);
            _intendedParent = _parentIndex < 0
                ? null
                : values[_parentIndex].Current;
        }

        /// <summary>Captures intended Parent and guarded values before the physical FK is suppressed.</summary>
        internal static CandidatePlan Capture(
            IUpdateEntry entry,
            object entity
        )
        {
            var readers = s_readers.GetValue(entry.EntityType, CreateReaders);
            var values = new GuardValue[readers.Length];

            // WHY: Snapshot once per affected candidate; repeated boundaries use typed reads without scalar boxing.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < readers.Length; index++)
            {
                values[index] = readers[index]
                    .Capture(entry, entity);
            }

            var parent = NestedSetModelMapping
                .For(entry.EntityType.Model)
                .Descriptor(entry.EntityType)
                .Parent
                .Resolve(entry.EntityType);

            return new CandidatePlan(values, entity, parent);
        }

        /// <summary>Freezes the suppressed physical FK while retaining the immutable requested Parent.</summary>
        internal void CaptureSuppressedParent(
            IUpdateEntry entry
        )
        {
            if (_parentIndex >= 0)
            {
                _values[_parentIndex] = _values[_parentIndex]
                    .Reader
                    .Capture(entry, _entity);
            }
        }

        /// <summary>Reads a Parent intent only for the exact dependent and mapped property that own it.</summary>
        internal bool TryParent(
            IProperty property,
            out object? value
        )
        {
            value = _intendedParent;

            return _parentIndex >= 0 && _values[_parentIndex].Reader.Property == property;
        }

        /// <summary>Combines role/order metadata once per mapped type instead of once per changed node.</summary>
        private static GuardReader[] CreateReaders(
            IEntityType entityType
        )
        {
            var model = NestedSetModelMapping.For(entityType.Model);
            var structure = model.StructuralProperties(entityType);
            var ordering = model.Ordering(entityType);

            return (ordering is null
                    ? structure
                    : structure
                        .Concat(ordering.Properties)
                        .Distinct())
                .Select(GuardReader.For)
                .ToArray();
        }

        /// <summary>Allows ordinary payload flags while preserving the exact hierarchy contract.</summary>
        internal bool Matches(
            IUpdateEntry entry
        )
        {
            if (entry.EntityState is not (EntityState.Unchanged or EntityState.Modified))
            {
                return false;
            }

            // WHY: The plan is an array; index iteration avoids interface enumerators at every save boundary.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _values.Length; index++)
            {
                if (!_values[index]
                    .Matches(entry, _entity))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Owns one scalar's independent values and its exact tracking flags.</summary>
    private readonly record struct GuardValue(
        GuardReader Reader,
        object? Current,
        object? Original,
        object? Clr,
        bool HasOriginal,
        bool Modified,
        bool Temporary,
        bool ConceptualNull
    )
    {
        /// <summary>Checks live sidecars and available originals without starting change detection.</summary>
        internal bool Matches(
            IUpdateEntry entry,
            object entity
        )
        {
            var property = Reader.Property;

            return entry.IsModified(property) == Modified
                && entry.HasTemporaryValue(property) == Temporary
                && entry.IsConceptualNull(property) == ConceptualNull
                && Reader.Matches(entry, Current, original: false)
                && Reader.MatchesClr(entity, Clr)
                && (!HasOriginal || Reader.Matches(entry, Original, original: true));
        }
    }

    /// <summary>Closes scalar reads once per mapped property instead of reflecting or boxing per boundary.</summary>
    private abstract class GuardReader
    {
        private static readonly ConditionalWeakTable<IProperty, GuardReader> s_readers = new();

        /// <summary>Retains finalized metadata without retaining a context.</summary>
        protected GuardReader(
            IProperty property
        )
        {
            Property = property;
        }

        /// <summary>Gets the exact property whose sidecars belong to this reader.</summary>
        internal IProperty Property { get; }

        /// <summary>Resolves one model-lifetime reader for a mapped scalar.</summary>
        internal static GuardReader For(
            IProperty property
        ) => s_readers.GetValue(
            property,
            static value => (GuardReader)Activator.CreateInstance(
                typeof(GuardReader<>).MakeGenericType(value.ClrType),
                value)!);

        /// <summary>Captures values through EF so shadow and generated sidecars keep their meaning.</summary>
        internal GuardValue Capture(
            IUpdateEntry entry,
            object entity
        )
        {
            var hasOriginal = entry.CanHaveOriginalValue(Property);
            var current = NestedSetStructuralValue.Snapshot(Property, entry.GetCurrentValue(Property));

            return new GuardValue(
                this,
                current,
                hasOriginal
                    ? NestedSetStructuralValue.Snapshot(Property, entry.GetOriginalValue(Property))
                    : null,
                SnapshotClr(entity, current),
                hasOriginal,
                entry.IsModified(Property),
                entry.HasTemporaryValue(Property),
                entry.IsConceptualNull(Property));
        }

        /// <summary>Compares a live scalar with its independent provider-aware plan snapshot.</summary>
        internal abstract bool Matches(
            IUpdateEntry entry,
            object? snapshot,
            bool original
        );

        /// <summary>Captures CLR storage independently when EF's current value comes from a sidecar.</summary>
        protected abstract object? SnapshotClr(
            object entity,
            object? current
        );

        /// <summary>Checks that callbacks retained the separately captured raw CLR representation.</summary>
        internal abstract bool MatchesClr(
            object entity,
            object? snapshot
        );
    }

    /// <summary>Keeps ordinary scalar comparisons typed while preserving custom CLR/model member mappings.</summary>
    private sealed class GuardReader<TValue> : GuardReader
        where TValue : notnull
    {
        private readonly Func<object, TValue>? _clr;
        private readonly NestedSetProviderComparer<TValue> _comparer;
        private readonly bool _typedReads;

        /// <summary>Captures the one property mapping for every plan using this model.</summary>
        public GuardReader(
            IProperty property
        ) : base(property)
        {
            _comparer = new NestedSetProviderComparer<TValue>(property);
            _clr = property.IsShadowProperty()
                ? null
                : NestedSetClrGetter<TValue>.For(property);

            _typedReads = property.IsShadowProperty()
                || property.GetMemberInfo(false, false) switch
                {
                    FieldInfo field => field.FieldType == typeof(TValue),
                    PropertyInfo member => member.PropertyType == typeof(TValue),
                    _ => false,
                };
        }

        /// <inheritdoc />
        internal override bool Matches(
            IUpdateEntry entry,
            object? snapshot,
            bool original
        )
        {
            if (!_typedReads)
            {
                // WHY: A CLR member whose type differs from EF metadata needs EF's exact object getter.
                var current = original
                    ? entry.GetOriginalValue(Property)
                    : entry.GetCurrentValue(Property);

                return _comparer.Matches(current, snapshot);
            }

            var value = original
                ? entry.GetOriginalValue<TValue>(Property)
                : entry.GetCurrentValue<TValue>(Property);

            return _comparer.Equals(value, (TValue)snapshot!);
        }

        /// <inheritdoc />
        protected override object? SnapshotClr(
            object entity,
            object? current
        )
        {
            if (_clr is null)
            {
                return null;
            }

            var value = _clr(entity);

            // WHY: Equal CLR/current values can share the already independent snapshot. A legitimate generated
            // sidecar/sentinel divergence needs a separate snapshot instead of being rejected or normalized.
            return _comparer.Equals(value, (TValue)current!)
                ? current
                : NestedSetTypedValue<TValue>.Snapshot(Property, value);
        }

        /// <inheritdoc />
        internal override bool MatchesClr(
            object entity,
            object? snapshot
        ) => _clr is null || _comparer.Equals(_clr(entity), (TValue)snapshot!);
    }
}
