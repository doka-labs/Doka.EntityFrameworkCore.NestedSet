namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Resolves the single relational table that owns all hierarchy columns.</summary>
internal static class NestedSetStoreObject
{
    /// <summary>Finds the writable table containing every structural and ordering property.</summary>
    /// <param name="entity">The configured hierarchy entity.</param>
    /// <param name="descriptor">The completed hierarchy metadata.</param>
    /// <returns>The one table that can serve every structural read and write.</returns>
    internal static StoreObjectIdentifier Resolve(
        IReadOnlyEntityType entity,
        NestedSetModelDescriptor descriptor
    )
    {
        var properties = descriptor
            .StructuralProperties
            .Concat(descriptor.Order.Select(order => order.Property))
            .Select(property => entity.FindProperty(property.Name)
                ?? throw new InvalidOperationException(
                    $"Nested-set property '{property.Name}' on '{entity.Name}' is not mapped."))
            .ToArray();

        return Resolve(entity, properties);
    }

    /// <summary>Finds the one table containing every supplied scalar property.</summary>
    /// <param name="entity">The entity whose table hierarchy and fragments are inspected.</param>
    /// <param name="properties">The scalar properties that must be co-located.</param>
    /// <returns>The unambiguous writable table.</returns>
    internal static StoreObjectIdentifier Resolve(
        IReadOnlyEntityType entity,
        IEnumerable<IReadOnlyProperty> properties
    )
    {
        var resolved = TryResolve(entity, properties);
        if (resolved is null)
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires every structural and ordering property in one "
                + "unambiguous writable table fragment.");
        }

        return resolved.Value;
    }

    /// <summary>Finds one table containing every supplied scalar property when the mutable map is complete.</summary>
    /// <param name="entity">The entity whose table hierarchy and fragments are inspected.</param>
    /// <param name="properties">The scalar properties that must be co-located.</param>
    /// <returns>The unambiguous table, or null while no single table can be identified.</returns>
    internal static StoreObjectIdentifier? TryResolve(
        IReadOnlyEntityType entity,
        IEnumerable<IReadOnlyProperty> properties
    )
    {
        var required = properties.ToArray();
        var candidates = CandidateTables(entity)
            .Where(store => required.All(property => property.GetColumnName(store) is not null))
            .Distinct()
            .Take(2)
            .ToArray();

        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>Enumerates primary and split table fragments from the complete inheritance chain.</summary>
    private static IEnumerable<StoreObjectIdentifier> CandidateTables(
        IReadOnlyEntityType entity
    )
    {
        for (var current = entity; current is not null; current = current.BaseType)
        {
            if (current.GetTableName() is { } table)
            {
                yield return StoreObjectIdentifier.Table(table, current.GetSchema());
            }

            foreach (var fragment in current.GetMappingFragments(StoreObjectType.Table))
            {
                yield return fragment.StoreObject;
            }
        }
    }
}
