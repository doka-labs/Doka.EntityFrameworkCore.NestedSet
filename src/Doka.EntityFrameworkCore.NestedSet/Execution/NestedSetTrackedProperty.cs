namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Respects the original-value storage chosen by the application's EF tracking strategy.</summary>
internal static class NestedSetTrackedProperty
{
    private static readonly ConditionalWeakTable<IProperty, ChangeReader> s_changes = new();

    /// <summary>Observes a mapped scalar without detecting changes or modifying its tracking state.</summary>
    /// <param name="entry">The provider entry whose live and original values belong to the same context.</param>
    /// <param name="property">The exact configured scalar metadata.</param>
    /// <returns>Whether the property is modified, conceptually null, or differs from its available original.</returns>
    internal static bool HasChanged(
        IUpdateEntry entry,
        IProperty property
    ) => entry.IsModified(property)
        || entry.IsConceptualNull(property)
        || (entry.CanHaveOriginalValue(property)
            && s_changes
                .GetValue(
                    property,
                    static value => (ChangeReader)Activator.CreateInstance(
                        typeof(ChangeReader<>).MakeGenericType(value.ClrType),
                        value)!)
                .HasChanged(entry));

    /// <summary>Finds a scalar entry through its complete mapped complex-property chain.</summary>
    /// <param name="entry">The tracked root entity that owns the scalar value.</param>
    /// <param name="property">The exact scalar metadata, including nested complex leaves.</param>
    /// <returns>The public EF property entry for current values, originals, and modification flags.</returns>
    internal static PropertyEntry Property(
        EntityEntry entry,
        IProperty property
    ) => property.DeclaringType is IComplexType complex
        ? ComplexProperty(entry, complex)
            .Property(property)
        : entry.Property(property.Name);

    /// <summary>Traverses model metadata instead of treating complex leaf names as root properties.</summary>
    private static ComplexPropertyEntry ComplexProperty(
        EntityEntry entry,
        IComplexType complex
    )
    {
        var property = complex.ComplexProperty;

        return property.DeclaringType is IComplexType parent
            ? ComplexProperty(entry, parent)
                .ComplexProperty(property)
            : entry.ComplexProperty(property);
    }

    /// <summary>Identifies properties for which EF allocates an original-value slot.</summary>
    /// <param name="property">The mapped property whose original value will be read or refreshed.</param>
    /// <returns>Whether the property's original value is available through its tracked entry.</returns>
    internal static bool HasOriginalValue(
        IProperty property
    )
    {
        // WHY: ChangingAndChangedNotifications omits originals except relationship and concurrency data.
        return property.DeclaringType.GetChangeTrackingStrategy()
            != ChangeTrackingStrategy.ChangingAndChangedNotifications
            || property.IsConcurrencyToken
            || property.IsKey()
            || property.IsForeignKey()
            || property
                .GetContainingIndexes()
                .Any(index => index.IsUnique);
    }

    /// <summary>Closes current/original comparisons once per property instead of boxing every tracked scalar.</summary>
    private abstract class ChangeReader
    {
        /// <summary>Compares live and original scalars using EF's configured value comparer.</summary>
        internal abstract bool HasChanged(
            IUpdateEntry entry
        );
    }

    /// <summary>Keeps scalar reads typed while retaining EF's member/model conversion semantics.</summary>
    private sealed class ChangeReader<TValue> : ChangeReader
    {
        private readonly ValueComparer _comparer;
        private readonly ValueComparer? _keyComparer;
        private readonly IProperty _property;
        private readonly Func<IUpdateEntry, TValue>? _relationship;
        private readonly ValueComparer<TValue>? _typedComparer;

        /// <summary>Captures immutable model metadata without retaining a context or entry.</summary>
        public ChangeReader(
            IProperty property
        )
        {
            _property = property;
            _comparer = property.GetValueComparer();
            var typedCurrent = property.IsShadowProperty()
                || property.GetMemberInfo(false, false) switch
                {
                    FieldInfo field => field.FieldType == typeof(TValue),
                    PropertyInfo member => member.PropertyType == typeof(TValue),
                    _ => false,
                };

            _typedComparer = typedCurrent
                ? _comparer as ValueComparer<TValue>
                : null;

            if (property.IsKey()
                || property.IsForeignKey())
            {
                // WHY: Replacing OriginalValue does not replace EF's installed relationship identity. Guards
                // must observe that identity too, just as EF's separate key-change detection does.
                _keyComparer = property.GetKeyValueComparer();
                _relationship = _typedComparer is not null && _keyComparer is ValueComparer<TValue>
                    ? NestedSetInsertionContract.Instance.RelationshipReader<TValue>(property)
                    : null;
            }
        }

        /// <inheritdoc />
        internal override bool HasChanged(
            IUpdateEntry entry
        )
        {
            if (_typedComparer is null)
            {
                // WHY: A custom non-generic comparer or differing CLR member type needs EF's exact object getters.
                var value = entry.GetCurrentValue(_property);

                return !_comparer.Equals(value, entry.GetOriginalValue(_property))
                    || (_keyComparer is not null
                        && !_keyComparer.Equals(value, entry.GetRelationshipSnapshotValue(_property)))
                    || HasChangedClrBehindSidecar(entry);
            }

            var current = entry.GetCurrentValue<TValue>(_property);

            return HasChangedTypedValue(entry, current, _typedComparer) || HasChangedClrBehindSidecar(entry);
        }

        /// <summary>Checks raw CLR storage only when an actual EF sidecar can conceal a later CLR edit.</summary>
        private bool HasChangedClrBehindSidecar(
            IUpdateEntry entry
        )
        {
            if (_property.IsShadowProperty()
                || !(entry.HasStoreGeneratedValue(_property) || entry.HasTemporaryValue(_property)))
            {
                return false;
            }

            // WHY: Sidecar-aware current reads can remain equal to the installed key after raw CLR mutation.
            // Only this exceptional branch needs an entity wrapper; normal unchanged comparisons stay typed.
            var entity = entry.ToEntityEntry()
                .Entity;

            var current = NestedSetClrGetter<TValue>
                .For(_property)(entity);

            return _typedComparer is null
                ? !_comparer.Equals(current, entry.GetOriginalValue(_property))
                || (_keyComparer is not null
                    && !_keyComparer.Equals(current, entry.GetRelationshipSnapshotValue(_property)))
                : HasChangedTypedValue(entry, current, _typedComparer);
        }

        /// <summary>Compares either typed live storage with the same available EF original and key snapshots.</summary>
        private bool HasChangedTypedValue(
            IUpdateEntry entry,
            TValue current,
            ValueComparer<TValue> comparer
        )
        {
            if (!EqualsValue(comparer, current, entry.GetOriginalValue<TValue>(_property)))
            {
                return true;
            }

            if (_keyComparer is null)
            {
                return false;
            }

            return _relationship is null
                ? !_keyComparer.Equals(current, entry.GetRelationshipSnapshotValue(_property))
                : !EqualsValue((ValueComparer<TValue>)_keyComparer, current, _relationship(entry));
        }

        /// <summary>Preserves EF's object-comparer null guards without boxing ordinary value types.</summary>
        private static bool EqualsValue(
                ValueComparer<TValue> comparer,
                TValue left,
                TValue right
            )

            // WHY: Typed equality invokes the custom expression directly; EF's object overload checks nulls first.
            => left is null
                ? right is null
                : right is not null && comparer.Equals(left, right);
    }
}
