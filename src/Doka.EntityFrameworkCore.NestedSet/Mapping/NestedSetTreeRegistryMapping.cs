namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Resolves immutable typed-registry metadata once for each finalized EF model.</summary>
internal sealed class NestedSetTreeRegistryMapping
{
    private static readonly ConditionalWeakTable<IModel, ModelCache> s_models = new();

    /// <summary>Creates one registry mapping from finalized, convention-validated metadata.</summary>
    /// <param name="hierarchy">The configured hierarchy owning the registry.</param>
    private NestedSetTreeRegistryMapping(
        IEntityType hierarchy
    )
    {
        var registryName = hierarchy.FindAnnotation(NestedSetAnnotationNames.TreeRegistryEntity)?.Value as string
            ?? throw new InvalidOperationException(
                $"Hierarchy '{hierarchy.Name}' has no typed tree registry. Use optionsBuilder.UseNestedSets().");

        Registry = hierarchy.Model.FindEntityType(registryName)
            ?? throw new InvalidOperationException(
                $"Hierarchy '{hierarchy.Name}' references a missing typed tree registry.");

        var descriptor = NestedSetModelDescriptor.FromFinalized(hierarchy);
        Hierarchy = hierarchy;
        SourceScope = descriptor.Scope?.Resolve(hierarchy);
        SourceTreeId = descriptor.TreeId.Resolve(hierarchy);
        Scope = Registry.FindProperty(NestedSetTreeRegistryMetadata.Scope);
        TreeId = RequiredProperty(NestedSetTreeRegistryMetadata.TreeId);
        Revision = RequiredProperty(NestedSetTreeRegistryMetadata.Revision);
        Lifecycle = RequiredProperty(NestedSetTreeRegistryMetadata.Lifecycle);

        if ((SourceScope is null) != (Scope is null))
        {
            throw new InvalidOperationException(
                $"Hierarchy '{hierarchy.Name}' and its tree registry disagree about Scope.");
        }

        RequireEquivalent(SourceScope, Scope);
        RequireEquivalent(SourceTreeId, TreeId);

        var tableName = Registry.GetTableName()
            ?? throw new InvalidOperationException("A physical tree-registry table is required.");

        Store = StoreObjectIdentifier.Table(tableName, Registry.GetSchema());
    }

    /// <summary>Gets the hierarchy that owns this registry.</summary>
    internal IEntityType Hierarchy { get; }

    /// <summary>Gets the named shared registry entity.</summary>
    internal IEntityType Registry { get; }

    /// <summary>Gets the optional hierarchy Scope property.</summary>
    internal IProperty? SourceScope { get; }

    /// <summary>Gets the hierarchy TreeId property.</summary>
    internal IProperty SourceTreeId { get; }

    /// <summary>Gets the optional registry Scope property.</summary>
    internal IProperty? Scope { get; }

    /// <summary>Gets the registry TreeId property.</summary>
    internal IProperty TreeId { get; }

    /// <summary>Gets the registry structural revision.</summary>
    internal IProperty Revision { get; }

    /// <summary>Gets the registry lifecycle status.</summary>
    internal IProperty Lifecycle { get; }

    /// <summary>Gets the registry's physical table identity.</summary>
    internal StoreObjectIdentifier Store { get; }

    /// <summary>Resolves the cached registry metadata for one hierarchy entity type.</summary>
    /// <param name="hierarchy">The finalized nested-set entity type.</param>
    /// <returns>The model-lifetime registry mapping.</returns>
    internal static NestedSetTreeRegistryMapping For(
        IEntityType hierarchy
    ) => s_models
        .GetValue(hierarchy.Model, static model => new ModelCache(model))
        .Mappings[hierarchy];

    /// <summary>Resolves one required registry property.</summary>
    /// <param name="name">The stable registry property name.</param>
    /// <returns>The mapped property.</returns>
    private IProperty RequiredProperty(
        string name
    ) => Registry.FindProperty(name)
        ?? throw new InvalidOperationException($"Tree-registry property '{name}' is not mapped.");

    /// <summary>Verifies that convention copying retained the source property's store identity.</summary>
    /// <param name="source">The source property, or null when Scope is absent.</param>
    /// <param name="target">The registry property, or null when Scope is absent.</param>
    private static void RequireEquivalent(
        IProperty? source,
        IProperty? target
    )
    {
        if (source is null
            && target is null)
        {
            return;
        }

        if (source is null
            || target is null
            || source.ClrType != target.ClrType
            || source.GetColumnType() != target.GetColumnType()
            || source
                .GetRelationalTypeMapping()
                .Converter
                ?.GetType()
            != target
                .GetRelationalTypeMapping()
                .Converter
                ?.GetType())
        {
            throw new InvalidOperationException(
                "The typed tree registry does not match its hierarchy identity mapping.");
        }
    }

    /// <summary>Indexes every hierarchy registry once without retaining a context.</summary>
    private sealed class ModelCache
    {
        /// <summary>Creates a read-only lookup for all configured hierarchies.</summary>
        /// <param name="model">The finalized model owning the metadata.</param>
        internal ModelCache(
            IModel model
        )
        {
            Mappings = model
                .GetEntityTypes()
                .Where(NestedSetModelValidator.IsNestedSet)
                .ToDictionary(entity => entity, entity => new NestedSetTreeRegistryMapping(entity));

            // WHY: A concrete TPH/TPT update entry shares the registry configured on its hierarchy base.
            // Cache the alias once so every lock request remains a direct metadata lookup.
            foreach (var entity in model.GetEntityTypes())
            {
                if (Mappings.ContainsKey(entity))
                {
                    continue;
                }

                for (var current = entity.BaseType; current is not null; current = current.BaseType)
                {
                    if (Mappings.TryGetValue(current, out var mapping))
                    {
                        Mappings.Add(entity, mapping);

                        break;
                    }
                }
            }
        }

        /// <summary>Gets registry metadata keyed by the owning hierarchy entity type.</summary>
        internal Dictionary<IEntityType, NestedSetTreeRegistryMapping> Mappings { get; }
    }
}
