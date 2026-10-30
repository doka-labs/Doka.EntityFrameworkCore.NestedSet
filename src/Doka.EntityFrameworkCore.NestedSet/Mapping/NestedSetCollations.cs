namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Resolves captured collation metadata without rebuilding convention-enabled or compiled models.</summary>
internal static class NestedSetCollations
{
    /// <summary>Captures hierarchy-specific string identities before EF removes relational collations.</summary>
    /// <param name="model">The convention model whose physical hierarchy stores have been validated.</param>
    /// <param name="provider">The selected native table-collation implementation.</param>
    /// <param name="typeMappings">The provider service resolving effective conversions before finalization.</param>
    internal static void Capture(
        IConventionModel model,
        NestedSetProviderCapabilities provider,
        ITypeMappingSource typeMappings
    )
    {
        foreach (var hierarchy in model.GetEntityTypes())
        {
            if (!NestedSetModelValidator.IsNestedSet(hierarchy))
            {
                continue;
            }

            var descriptor = NestedSetModelValidator.Validate(hierarchy, true)!;
            var store = NestedSetStoreObject.Resolve(hierarchy, descriptor);
            NestedSetIdentityLength.Capture(hierarchy, descriptor, provider, typeMappings);

            foreach (var role in new[]
                     {
                         NestedSetPropertyRoles.NodeKey,
                         NestedSetPropertyRoles.Parent,
                         NestedSetPropertyRoles.Scope,
                         NestedSetPropertyRoles.TreeId,
                     })
            {
                if (hierarchy.FindAnnotation(NestedSetAnnotationNames.Prefix + role)?.Value is not string name
                    || hierarchy.FindProperty(name) is not { } property
                    || !HasStringStore(property, typeMappings))
                {
                    continue;
                }

                // WHY: Concrete TPC hierarchies can share one inherited IProperty while its physical columns
                // have different collations. Capture on the configured owner, never on that shared property.
                hierarchy.SetAnnotation(
                    AnnotationName(property.Name),
                    ResolveEffective(hierarchy, property, store, provider, typeMappings) ?? string.Empty);
            }
        }

        model.SetAnnotation(NestedSetAnnotationNames.CollationsCaptured, true);
    }

    /// <summary>Returns the exact hierarchy column's effective collation or an unknown database default.</summary>
    /// <param name="context">The context whose finalized model owns the hierarchy.</param>
    /// <param name="property">The configured identity property, including an inherited property.</param>
    /// <param name="hierarchy">The configured hierarchy whose physical structural store owns the column.</param>
    /// <returns>The effective physical-column, supported table, or model collation; otherwise null.</returns>
    /// <exception cref="InvalidOperationException">A supplied model lacks finalized owner metadata.</exception>
    internal static string? Resolve(
        DbContext context,
        IProperty property,
        IEntityType hierarchy
    )
    {
        if (!HasStringStore(property))
        {
            return null;
        }

        // WHY: A concrete TPH/TPT query or update entry shares its configured ancestor's registry and captures.
        // A directly configured concrete TPC hierarchy owns its own physical facets despite inherited properties.
        for (var current = hierarchy; current is not null; current = current.BaseType)
        {
            if (NestedSetModelValidator.IsNestedSet(current))
            {
                hierarchy = current;

                break;
            }
        }

        if (context.Model.FindAnnotation(NestedSetAnnotationNames.CollationsCaptured)
                ?.Value is true)
        {
            var captured = hierarchy.FindAnnotation(AnnotationName(property.Name))?.Value as string
                ?? throw new InvalidOperationException(
                    $"The nested-set collation capture for '{hierarchy.Name}.{property.Name}' is incomplete. "
                    + "Regenerate after configuring the native provider callback's UseNestedSets() before model generation "
                    + "so hierarchy-owned physical identity collations are captured.");

            return captured.Length == 0 ? null : captured;
        }

        if (context
                .GetService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>()
                ?.Model is not null
            || context.Model.GetType() != typeof(RuntimeModel))
        {
            // WHY: Reconstructing a supplied runtime model defeats compiled-model reuse and can change semantics.
            throw new InvalidOperationException(
                "Compiled or explicitly supplied models require the native provider callback's UseNestedSets() before model "
                + "generation. Regenerate the model so hierarchy-owned physical collation metadata is captured.");
        }

        // WHY: Legacy convention-free contexts need their design model; a supplied model must never enter here.
        // This fallback runs while constructing immutable mapping/query metadata, not within a mutation loop.
        var design = context.GetService<IDesignTimeModel>().Model;
        var designHierarchy = design.FindEntityType(hierarchy.Name)
            ?? throw new InvalidOperationException("The nested-set design-time hierarchy is not mapped.");

        var designProperty = designHierarchy.FindProperty(property.Name)
            ?? throw new InvalidOperationException("The nested-set design-time identity property is not mapped.");

        var descriptor = NestedSetModelDescriptor.FromFinalized(designHierarchy);
        var store = NestedSetStoreObject.Resolve(designHierarchy, descriptor);
        var provider = NestedSetProviderCapabilities.Resolve(context);

        return ResolveEffective(designHierarchy, designProperty, store, provider);
    }

    /// <summary>Resolves one design-time facet shared by runtime capture and registry identity construction.</summary>
    /// <param name="hierarchy">The configured owner of the exact structural table.</param>
    /// <param name="property">The identity property whose physical column is inspected.</param>
    /// <param name="store">The validated structural table, including an entity-splitting fragment.</param>
    /// <param name="provider">The selected native table-collation implementation.</param>
    /// <param name="typeMappings">The provider service when type mappings are not yet finalized.</param>
    /// <returns>The physical-column, supported table, or model collation; otherwise null.</returns>
    internal static string? ResolveEffective(
        IReadOnlyEntityType hierarchy,
        IReadOnlyProperty property,
        StoreObjectIdentifier store,
        NestedSetProviderCapabilities provider,
        ITypeMappingSource? typeMappings = null
    )
    {
        if (!HasStringStore(property, typeMappings))
        {
            return null;
        }

        // WHY: Shared-column roots and store-specific overrides belong to EF's physical-column metadata.
        // An explicit column facet must take precedence over every table or model default.
        if (property.GetCollation(store) is { } columnCollation)
        {
            return columnCollation;
        }

        if (provider.ResolveTableCollation(hierarchy.Model, store) is { } tableCollation)
        {
            return tableCollation;
        }

        return hierarchy.Model.GetCollation();
    }

    /// <summary>Checks the effective converter, including conversions selected by the relational store type.</summary>
    private static bool HasStringStore(
        IReadOnlyProperty property,
        ITypeMappingSource? typeMappings = null
    )
    {
        // WHY: HasConversion metadata omits an enum-to-text converter selected solely by a store type. Runtime
        // mappings are already cached; conventions ask the same provider service before those mappings exist.
        var mapping = property.FindTypeMapping()
            ?? (property is IProperty mapped && typeMappings is not null ? typeMappings.FindMapping(mapped) : null);

        return (mapping?.Converter?.ProviderClrType
            ?? property.GetValueConverter()?.ProviderClrType
            ?? property.GetProviderClrType() ?? property.ClrType) == typeof(string);
    }

    /// <summary>Keys the runtime-safe facet by property within its exact hierarchy owner.</summary>
    private static string AnnotationName(
        string propertyName
    ) => NestedSetAnnotationNames.Collation + ":" + propertyName;
}
