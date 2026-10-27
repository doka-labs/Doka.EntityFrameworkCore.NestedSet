namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Captures insertion-owned CLR values without retaining EF entries or context-scoped services.</summary>
internal static class NestedSetInsertionValues
{
    private static readonly ConditionalWeakTable<IEntityType, Shape> s_shapes = new();
    private static readonly object s_missing = new();

    /// <summary>Represents an originally absent mapped dictionary entry independently of all CLR sentinels.</summary>
    internal static object MissingValue => s_missing;

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

        // WHY: Keep each captured property aligned with its compiled CLR setter.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < shape.Properties.Length; index++)
        {
            var property = shape.Properties[index];

            if (excluded?.Contains(property) == true)
            {
                continue;
            }

            var original = SnapshotValue(property, ReadClr(property, entity));

            (values ??= []).Add(new Value(property, shape.Setters[index], original));
        }

        // WHY: Generated leaves and ownership FKs can change before a later wave fails. Capture only these
        // insertion-owned roles; ordinary payload and non-ownership business FKs remain application-owned.
        return values is null ? null : new Snapshot(entity, values.ToArray());
    }

    /// <summary>Reads raw mapped dictionary values without normalizing missing or null entries through EF getters.</summary>
    /// <param name="property">The mapped CLR or indexer property.</param>
    /// <param name="entity">The containing root object used by EF's configured accessor.</param>
    /// <returns>The original representation, a missing-entry marker, or the sentinel for shadow state.</returns>
    internal static object? ReadClr(
        IProperty property,
        object entity
    )
    {
        if (property.IsIndexerProperty()
            && entity is IDictionary<string, object?> dictionary)
        {
            return dictionary.TryGetValue(property.Name, out var value) ? value : s_missing;
        }

        return property.IsShadowProperty()
            ? property.Sentinel
            : property
                .GetGetter()
                .GetClrValueUsingContainingEntity(entity);
    }

    /// <summary>Distinguishes an absent mapped dictionary entry from every present value, including null.</summary>
    internal static bool IsMissing(
        object? value
    ) => ReferenceEquals(value, s_missing);

    /// <summary>Copies mapped mutable values while preserving the distinct absent-entry marker.</summary>
    internal static object? SnapshotValue(
        IProperty property,
        object? value
    ) => IsMissing(value) ? value : NestedSetStructuralValue.Snapshot(property, value);

    /// <summary>Preserves EF's raw dictionary sentinel semantics before generated identity assignment.</summary>
    /// <typeparam name="TKey">The typed normalized node identity.</typeparam>
    /// <param name="property">The exact mapped node-key property.</param>
    /// <param name="entity">The detached caller entity before any generated values are assigned.</param>
    /// <param name="value">The normalized identity already read for the insertion plan.</param>
    /// <returns>Whether the caller supplied an identity that must remain immutable during insertion.</returns>
    internal static bool HasAssignedKey<TKey>(
        IProperty property,
        object entity,
        TKey value
    )
    {
        if (property.ValueGenerated == ValueGenerated.Never)
        {
            return true;
        }

        // WHY: A required integer indexer's raw sentinel can be null while its typed getter returns zero.
        // EF's public sentinel accessor preserves that distinction; ordinary CLR/shadow reads keep their policy.
        return property.IsIndexerProperty() && entity is IDictionary<string, object?>
            ? !property
                .GetGetter()
                .HasSentinelValueUsingContainingEntity(entity)
            : !property
                .GetValueComparer()
                .Equals(value, property.Sentinel);
    }

    /// <summary>Restores one raw mapped dictionary value or removes an originally absent key.</summary>
    /// <param name="entity">The caller-owned containing entity.</param>
    /// <param name="property">The exact mapped indexer whose membership is owned by rollback.</param>
    /// <param name="original">The independent pre-write representation or missing-entry marker.</param>
    /// <returns>Whether dictionary semantics applied; other CLR and custom indexer setters remain unchanged.</returns>
    internal static bool RestoreDictionaryValue(
        object entity,
        IProperty property,
        object? original
    )
    {
        if (!property.IsIndexerProperty()
            || entity is not IDictionary<string, object?> dictionary)
        {
            return false;
        }

        // WHY: EF's typed indexer getter/setter maps both absence and a present null to a CLR default.
        // Restore only this mapped key directly so rollback preserves membership without copying payload.
        if (IsMissing(original))
        {
            dictionary.Remove(property.Name);
        }
        else
        {
            dictionary[property.Name] = NestedSetStructuralValue.Snapshot(property, original);
        }

        return true;
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
                    if (!RestoreDictionaryValue(_entity, value.Property, value.Original))
                    {
                        value.Setter.SetClrValueUsingContainingEntity(
                            _entity,
                            NestedSetStructuralValue.Snapshot(value.Property, value.Original));
                    }
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
