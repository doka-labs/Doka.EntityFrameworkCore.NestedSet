namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Captures insertion-owned CLR values without retaining EF entries or context-scoped services.</summary>
internal static class NestedSetInsertionValues
{
    private static readonly ConditionalWeakTable<IEntityType, Shape> s_shapes = new();

    /// <summary>Snapshots generated leaves and owned identities not already owned by structural rollback.</summary>
    /// <param name="metadata">The exact root, derived, named, or owned entity metadata.</param>
    /// <param name="entity">The caller-owned input whose CLR values may change during fixup or persistence.</param>
    /// <param name="excluded">Structural properties already captured independently, or no exclusions.</param>
    /// <returns>A snapshot, or no state when the entity has no applicable insertion-owned CLR values.</returns>
    internal static Snapshot? Capture(
        IEntityType metadata,
        object entity,
        IReadOnlySet<IProperty>? excluded = null
    )
    {
        var shape = s_shapes.GetValue(metadata, CreateShape);
        List<Value>? values = null;

        for (var index = 0; index < shape.Properties.Length; index++)
        {
            var property = shape.Properties[index];

            if (excluded?.Contains(property) == true)
            {
                continue;
            }

            var original = NestedSetStructuralValue.Snapshot(
                property,
                property
                    .GetGetter()
                    .GetClrValueUsingContainingEntity(entity));

            (values ??= []).Add(new Value(property, shape.Setters[index], original));
        }

        // WHY: Generated leaves and ownership FKs can change before a later wave fails. Capture only these
        // insertion-owned roles; ordinary payload and non-ownership business FKs remain application-owned.
        return values is null ? null : new Snapshot(entity, values.ToArray());
    }

    /// <summary>Resolves metadata-only CLR setters once before fixup or generated values can change inputs.</summary>
    private static Shape CreateShape(
        IEntityType metadata
    )
    {
        var ownershipProperties = metadata
            .GetForeignKeys()
            .Where(foreignKey => foreignKey.IsOwnership)
            .SelectMany(foreignKey => foreignKey.Properties)
            .ToHashSet();

        if (metadata.IsOwned())
        {
            ownershipProperties.UnionWith(
                metadata
                    .GetKeys()
                    .SelectMany(key => key.Properties));
        }

        var properties = metadata
            .GetFlattenedProperties()
            .Where(property => !property.IsShadowProperty()
                && (property.ValueGenerated != ValueGenerated.Never || ownershipProperties.Contains(property)))
            .ToArray();

        return new Shape(
            properties,
            properties
                .Select(property => NestedSetDetachedValueSetter.Get(property)!)
                .ToArray());
    }

    /// <summary>Retains only the caller entity and insertion-owned values until completion or rollback.</summary>
    internal sealed class Snapshot
    {
        private readonly object _entity;
        private readonly Value[] _values;

        /// <summary>Uses independent values captured before the insertion's first tracking transition.</summary>
        /// <param name="entity">The caller entity whose insertion-owned CLR values belong to this snapshot.</param>
        /// <param name="values">The mapped leaves and independent original representations.</param>
        internal Snapshot(
            object entity,
            Value[] values
        )
        {
            _entity = entity;
            _values = values;
        }

        /// <summary>Restores every insertion-owned value while retaining rejecting application setter errors.</summary>
        internal void Restore()
        {
            List<Exception>? errors = null;

            foreach (var value in _values)
            {
                try
                {
                    value.Setter.SetClrValueUsingContainingEntity(
                        _entity,
                        NestedSetStructuralValue.Snapshot(value.Property, value.Original));
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }

            if (errors is not null)
            {
                throw new AggregateException(
                    "Insertion-owned values could not be restored. Discard the context.",
                    errors);
            }
        }
    }

    /// <summary>Shares finalized property and setter arrays for the lifetime of their metadata.</summary>
    private sealed record Shape(
        IProperty[] Properties,
        IClrPropertySetter[] Setters
    );

    /// <summary>Retains one exact insertion-owned CLR value and its independent original representation.</summary>
    /// <param name="Property">The finalized insertion-owned CLR leaf metadata.</param>
    /// <param name="Setter">The mapped containing-entity setter shared with EF.</param>
    /// <param name="Original">The independent pre-insertion CLR value.</param>
    internal readonly record struct Value(
        IProperty Property,
        IClrPropertySetter Setter,
        object? Original
    );
}
