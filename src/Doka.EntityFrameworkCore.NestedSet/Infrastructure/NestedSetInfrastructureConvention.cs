namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Adds one typed, library-owned tree registry for each configured hierarchy.</summary>
internal sealed class NestedSetInfrastructureConvention : IModelFinalizingConvention
{
    /// <summary>The official registered provider identity controlling canonical table collation support.</summary>
    private readonly string _providerName;

    /// <summary>The provider service resolving the exact registry identity's effective conversion.</summary>
    private readonly ITypeMappingSource _typeMappings;

    /// <summary>Creates registry metadata conventions for the registered database provider.</summary>
    /// <param name="providerName">The official registered database provider name.</param>
    /// <param name="typeMappings">The provider's effective property-mapping service.</param>
    internal NestedSetInfrastructureConvention(
        string providerName,
        ITypeMappingSource typeMappings
    )
    {
        _providerName = providerName;
        _typeMappings = typeMappings;
    }

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        var hierarchies = modelBuilder
            .Metadata
            .GetEntityTypes()
            .Where(NestedSetModelValidator.IsNestedSet)
            .ToArray();

        foreach (var hierarchy in hierarchies)
        {
            AddRegistry(modelBuilder, hierarchy, _providerName, _typeMappings);
        }
    }

    /// <summary>Maps one shared infrastructure entity whose key matches the hierarchy's database identity.</summary>
    /// <param name="modelBuilder">The convention model receiving the registry entity.</param>
    /// <param name="hierarchy">The already validated nested-set hierarchy.</param>
    /// <param name="providerName">The official registered database provider name.</param>
    /// <param name="typeMappings">The provider's effective property-mapping service.</param>
    private static void AddRegistry(
        IConventionModelBuilder modelBuilder,
        IConventionEntityType hierarchy,
        string providerName,
        ITypeMappingSource typeMappings
    )
    {
        var descriptor = NestedSetModelValidator.Validate(hierarchy, true)
            ?? throw new InvalidOperationException($"Nested-set entity '{hierarchy.Name}' is incomplete.");

        var entityName = NestedSetTreeRegistryMetadata.EntityName(hierarchy.Name);
        var registry = modelBuilder.SharedTypeEntity(entityName, typeof(NestedSetTreeRegistry), false, true)
            ?? throw new InvalidOperationException($"The EF model rejected the tree registry for '{hierarchy.Name}'.");

        // WHY: The annotation gives runtime and generated models an exact lookup without scanning infrastructure
        // entity types or reconstructing naming rules from potentially renamed application tables.
        hierarchy.SetAnnotation(NestedSetAnnotationNames.TreeRegistryEntity, entityName, true);
        registry.Metadata.SetAnnotation(NestedSetAnnotationNames.TreeRegistryOwner, hierarchy.Name, true);
        var hierarchyStore = NestedSetStoreObject.Resolve(hierarchy, descriptor);
        var nodeKeyColumn = descriptor
                .NodeKey
                .Resolve(hierarchy)
                .GetColumnName(hierarchyStore)
            ?? throw new InvalidOperationException("The nested-set node key has no hierarchy-table column.");

        var scopeColumn = descriptor
            .Scope
            ?.Resolve(hierarchy)
            .GetColumnName(hierarchyStore);

        var treeIdColumn = descriptor
                .TreeId
                .Resolve(hierarchy)
                .GetColumnName(hierarchyStore)
            ?? throw new InvalidOperationException("The nested-set TreeId has no hierarchy-table column.");

        registry.Metadata.SetTableName(
            NestedSetTreeRegistryMetadata.TableName(
                hierarchyStore.Schema,
                hierarchyStore.Name,
                nodeKeyColumn,
                scopeColumn,
                treeIdColumn),
            true);

        registry.Metadata.SetSchema(hierarchyStore.Schema, true);

        var key = new List<IConventionProperty>();

        if (descriptor.Scope is { } scopeDescriptor)
        {
            var scopeProperty = scopeDescriptor.Resolve(hierarchy);

            key.Add(
                AddIdentityProperty(
                    registry,
                    scopeProperty,
                    NestedSetTreeRegistryMetadata.Scope,
                    NestedSetCollations.ResolveEffective(
                        hierarchy, scopeProperty, hierarchyStore, providerName, typeMappings)));
        }

        var treeProperty = descriptor.TreeId.Resolve(hierarchy);

        key.Add(
            AddIdentityProperty(
                registry,
                treeProperty,
                NestedSetTreeRegistryMetadata.TreeId,
                NestedSetCollations.ResolveEffective(
                    hierarchy,
                    treeProperty,
                    hierarchyStore,
                    providerName,
                    typeMappings)));

        var revision = registry.Property(typeof(long), NestedSetTreeRegistryMetadata.Revision, false, true)
            ?? throw new InvalidOperationException("The EF model rejected the tree-registry revision.");

        var lifecycle = registry.Property(typeof(byte), NestedSetTreeRegistryMetadata.Lifecycle, false, true)
            ?? throw new InvalidOperationException("The EF model rejected the tree-registry lifecycle.");

        revision.IsRequired(true, true);
        revision.ValueGenerated(ValueGenerated.Never, true);
        lifecycle.IsRequired(true, true);
        lifecycle.ValueGenerated(ValueGenerated.Never, true);
        registry.PrimaryKey(key, true);
    }

    /// <summary>Copies identity facets that affect database equality, ordering, or parameter binding.</summary>
    /// <param name="registry">The named shared registry entity.</param>
    /// <param name="source">The hierarchy property providing native store semantics.</param>
    /// <param name="name">The stable registry property and column name.</param>
    /// <param name="collation">The effective source column collation, or an unknown database default.</param>
    /// <returns>The non-null registry identity property.</returns>
    private static IConventionProperty AddIdentityProperty(
        IConventionEntityTypeBuilder registry,
        IConventionProperty source,
        string name,
        string? collation
    )
    {
        var property = registry.Property(source.ClrType, name, false, true)
            ?? throw new InvalidOperationException($"The EF model rejected tree-registry property '{name}'.");

        property.IsRequired(true, true);
        property.ValueGenerated(ValueGenerated.Never, true);

        // WHY: Equal CLR values can have different store representations after conversion, and string equality
        // follows collation. The registry must use the node column's exact database identity semantics.
        // WHY: Model-finalizing conventions run before EF initializes relational type mappings. A conversion
        // configured with HasConversion<TProvider>() is represented by ProviderClrType at this stage, while a
        // converter instance is represented by ValueConverter. Copy both pre-runtime metadata shapes so EF can
        // independently construct an equivalent relational mapping for the registry property.
        property.Metadata.SetValueConverter(source.GetValueConverter(), true);
        property.Metadata.SetProviderClrType(source.GetProviderClrType(), true);
        property.Metadata.SetValueComparer(source.GetValueComparer(), true);

        // WHY: Before relational mappings are initialized, EF can return the model comparer as the provider
        // fallback. Copying it to a converted registry property gives its provider column the wrong CLR type.
        if (source.GetProviderValueComparerConfigurationSource() is not null)
        {
            property.Metadata.SetProviderValueComparer(source.GetProviderValueComparer(), true);
        }

        property.Metadata.SetMaxLength(source.GetMaxLength(), true);
        property.Metadata.SetIsUnicode(source.IsUnicode(), true);
        property.Metadata.SetPrecision(source.GetPrecision(), true);
        property.Metadata.SetScale(source.GetScale(), true);
        property.Metadata.SetColumnType(source.GetColumnType(), true);

        // WHY: A table default is part of the source column's identity even when the property facet is absent.
        // Reuse the captured-facet resolver so registry keys and request grouping cannot disagree about it.
        property.Metadata.SetCollation(collation, true);
        property.Metadata.SetIsFixedLength(source.IsFixedLength(), true);

        return property.Metadata;
    }
}
