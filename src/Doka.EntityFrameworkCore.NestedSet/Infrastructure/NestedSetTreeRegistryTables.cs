namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Rejects explicit registry table names that the selected provider cannot keep apart.</summary>
internal static class NestedSetTreeRegistryTables
{
    /// <summary>Checks limits, inheritance conflicts and physical collisions once all registries exist.</summary>
    /// <param name="model">The finalizing model containing registries and application tables.</param>
    /// <param name="hierarchies">The configured hierarchies owning those registries.</param>
    /// <param name="provider">The selected native provider implementation.</param>
    /// <exception cref="InvalidOperationException">An explicit registry name cannot be honored.</exception>
    internal static void Validate(
        IConventionModel model,
        IReadOnlyList<IConventionEntityType> hierarchies,
        NestedSetProviderCapabilities provider
    )
    {
        foreach (var hierarchy in hierarchies)
        {
            if (Explicit(hierarchy) is not { } name)
            {
                continue;
            }

            if (provider.IsReservedTableName(name))
            {
                throw new InvalidOperationException(
                    $"Tree-registry table name '{name}' on '{hierarchy.DisplayName()}' is reserved by this provider.");
            }

            ValidateLength(hierarchy, name, provider);
            ValidateInheritance(hierarchy, name);
            ValidateCollisions(model, hierarchy, name, provider);
        }
    }

    /// <summary>Reads the explicit name configured directly on one hierarchy.</summary>
    private static string? Explicit(
        IReadOnlyEntityType hierarchy
    ) => hierarchy.FindAnnotation(NestedSetAnnotationNames.TreeRegistryTable)?.Value as string;

    /// <summary>Applies the provider's documented identifier limit in its own unit.</summary>
    private static void ValidateLength(
        IConventionEntityType hierarchy,
        string name,
        NestedSetProviderCapabilities provider
    )
    {
        if (provider.MaxIdentifierLength is not { } limit)
        {
            return;
        }

        var length = provider.IdentifierLengthCountsBytes ? Encoding.UTF8.GetByteCount(name) : name.Length;

        if (length > limit)
        {
            var unit = provider.IdentifierLengthCountsBytes ? "bytes" : "characters";

            throw new InvalidOperationException(
                $"Tree-registry table name '{name}' on '{hierarchy.DisplayName()}' has {length} {unit}; "
                + $"this provider allows {limit}.");
        }
    }

    /// <summary>Requires one registry name across every nested-set configuration of one inheritance tree.</summary>
    private static void ValidateInheritance(
        IConventionEntityType hierarchy,
        string name
    )
    {
        for (var current = hierarchy.BaseType; current is not null; current = current.BaseType)
        {
            if (NestedSetModelValidator.IsNestedSet(current)
                && Explicit(current) != name)
            {
                throw new InvalidOperationException(
                    $"Tree-registry table name '{name}' on '{hierarchy.DisplayName()}' conflicts with "
                    + $"'{Explicit(current) ?? "the generated name"}' on its base type '{current.DisplayName()}'.");
            }
        }
    }

    /// <summary>Rejects any other table in the same schema that the provider could treat as this one.</summary>
    private static void ValidateCollisions(
        IConventionModel model,
        IConventionEntityType hierarchy,
        string name,
        NestedSetProviderCapabilities provider
    )
    {
        var registry = model.FindEntityType(NestedSetTreeRegistryMetadata.EntityName(hierarchy.Name))!;
        var schema = registry.GetSchema();

        foreach (var entity in model.GetEntityTypes())
        {
            if (ReferenceEquals(entity, registry))
            {
                continue;
            }

            foreach (var table in Tables(entity))
            {
                // WHY: A provider that drops schemas from DDL creates both tables under the same bare name.
                if (provider.IdentifierComparer.Equals(table.Name, name)
                    && (!provider.HonorsSchemas || provider.IdentifierComparer.Equals(table.Schema, schema)))
                {
                    throw new InvalidOperationException(
                        $"Tree-registry table name '{name}' on '{hierarchy.DisplayName()}' collides with "
                        + $"'{table.Name}' mapped by '{entity.DisplayName()}'. Each registry needs its own table.");
                }
            }
        }
    }

    /// <summary>Enumerates the physical tables and views an entity type occupies, including split fragments.</summary>
    private static IEnumerable<(string Name, string? Schema)> Tables(
        IConventionEntityType entity
    )
    {
        if (entity.GetTableName() is { } table)
        {
            yield return (table, entity.GetSchema());
        }

        if (entity.GetViewName() is { } view)
        {
            yield return (view, entity.GetViewSchema());
        }

        foreach (var fragment in entity.GetMappingFragments(StoreObjectType.Table))
        {
            yield return (fragment.StoreObject.Name, fragment.StoreObject.Schema);
        }
    }
}
