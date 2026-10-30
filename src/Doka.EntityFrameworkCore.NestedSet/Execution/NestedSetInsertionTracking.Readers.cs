namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal static partial class NestedSetInsertionTracking
{
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

        // WHY: Fill typed readers directly at their corresponding key-property ordinals.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
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
        // WHY: Compare property-specific key comparers and both value vectors at matching ordinals.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
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
}
