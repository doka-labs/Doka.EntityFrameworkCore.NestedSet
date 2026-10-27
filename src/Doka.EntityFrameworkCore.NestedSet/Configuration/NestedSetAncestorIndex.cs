namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Owns the optional generated ancestor flag while preserving application metadata ownership.</summary>
internal static class NestedSetAncestorIndex
{
    /// <summary>Tracks only properties introduced by this library while their mutable model remains alive.</summary>
    private static readonly ConditionalWeakTable<IConventionEntityType, List<IConventionProperty>> s_owned = new();

    /// <summary>Creates a convention-owned shadow flag without requiring provider services during configuration.</summary>
    /// <param name="entity">The mutable hierarchy metadata.</param>
    /// <param name="finalizing">Whether inheritance and table selections have finished application configuration.</param>
    internal static void EnsureProperty(
        IConventionEntityType entity,
        bool finalizing = false
    )
    {
        if (Name(entity) is not { } name)
        {
            return;
        }

        var property = entity.FindProperty(name);

        if (property is null)
        {
            if (entity.FindIgnoredConfigurationSource(name) is not null)
            {
                throw Invalid(entity, name, "is not mapped because the application ignored it");
            }

            if (!finalizing)
            {
                // WHY: Final declaration follows all application inheritance and fragment choices. The early
                // finalizer still introduces the flag and its full index before provider key-length budgeting.
                return;
            }

            // WHY: Bounds inherited from a mapped base live in that base table under TPT. Declaring the flag
            // with those bounds allows the generated expression and its index to share the writable fragment.
            var left = entity.FindAnnotation(NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.Left)
                ?.Value is not string leftName
                ? null
                : entity.FindProperty(leftName);

            var right = entity.FindAnnotation(NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.Right)
                ?.Value is not string rightName
                ? null
                : entity.FindProperty(rightName);

            var declaring = entity.GetMappingStrategy() != RelationalAnnotationNames.TpcMappingStrategy
                && left?.DeclaringType is IConventionEntityType owner
                && owner == right?.DeclaringType
                    ? owner
                    : entity;

            var builder = declaring
                .Builder
                .Property(typeof(bool), name, false)
                ?.IsRequired(true);

            property = builder?.Metadata
                ?? throw Invalid(entity, name, "could not create its required Boolean property");

            s_owned
                .GetValue(entity, _ => [])
                .Add(property);
        }

        ValidateType(entity, property);
    }

    /// <summary>Derives provider-correct generated SQL from the final quoted physical boundary columns.</summary>
    /// <param name="entity">The completed hierarchy metadata.</param>
    /// <param name="mapping">The validated structural descriptor.</param>
    /// <param name="sql">The provider's identifier service.</param>
    /// <param name="provider">The selected native generated-column implementation.</param>
    /// <param name="typeMappings">The provider service resolving implicit storage conversions.</param>
    internal static void Reconcile(
        IConventionEntityType entity,
        NestedSetModelDescriptor mapping,
        ISqlGenerationHelper sql,
        NestedSetProviderCapabilities provider,
        ITypeMappingSource typeMappings
    )
    {
        if (mapping.AncestorIndexProperty is not { } descriptor)
        {
            return;
        }

        var property = descriptor.Resolve(entity);
        ValidateType(entity, property);

        if (property.IsIndexerProperty()
            && property.PropertyInfo?.PropertyType == typeof(object)
            && property.Sentinel is not null)
        {
            throw Invalid(entity, property.Name, "requires the indexer's natural null missing-key sentinel");
        }

        if (property is IProperty mapped
            && typeMappings.FindMapping(mapped)
                ?.Converter is not null)
        {
            throw Invalid(entity, property.Name, "cannot use a provider-selected value converter");
        }

        var store = ValidatePlacement(entity, mapping);

        if (property.GetColumnName(store) is null)
        {
            // WHY: Entity splitting can be configured after HasNestedSet. An explicit table override joins
            // the generated flag to the completed structure fragment instead of referencing another table.
            property.SetColumnName(property.GetColumnName(), store);
        }

        var left = sql.DelimitIdentifier(
            mapping
                .Left
                .Resolve(entity)
                .GetColumnName(store)!);

        var right = sql.DelimitIdentifier(
            mapping
                .Right
                .Resolve(entity)
                .GetColumnName(store)!);

        var atUpperLimit = $"{left} = {long.MaxValue.ToString(CultureInfo.InvariantCulture)}";
        var nextBoundary = $"CASE WHEN {atUpperLimit} THEN {left} ELSE {left} + 1 END";
        var predicate = $"{right} > {nextBoundary}";

        // WHY: Split fragments can retain repairable empty/reversed signed intervals without table CHECKs.
        // Left + 1 overflows only at Int64.MaxValue, where no signed Right can make the predicate true.
        // Scalar CASE guards that addition; SQL AND does not guarantee left-to-right evaluation.
        var expression = provider.AncestorFlagSql(predicate);
        var stored = provider.StoresAncestorFlag;
        ValidateGeneration(entity, property, expression, stored);

        if (property.Builder.HasComputedColumnSql(expression) is null
            || property.Builder.IsStoredComputedColumn(stored) is null
            || property.Builder.ValueGenerated(ValueGenerated.OnAddOrUpdate) is null
            || property.Builder.BeforeSave(PropertySaveBehavior.Ignore) is null
            || property.Builder.AfterSave(PropertySaveBehavior.Ignore) is null)
        {
            throw Invalid(entity, property.Name, "conflicts with application generation or save behavior");
        }

        if (!property.IsIndexerProperty()
            && property.GetSentinelConfigurationSource() is null)
        {
            // WHY: This always-generated flag never chooses between a CLR value and a database default.
            // An explicit false sentinel lets EF 10 validate split-table Boolean metadata without looking
            // for a default on the entity's different primary table. Indexers retain their natural sentinel:
            // a dictionary's object indexer uses null to return default(bool) safely for an absent key.
            property.Builder.HasSentinel(false);
        }
    }

    /// <summary>Preserves application fragment choices before provider finalizers normalize redundant overrides.</summary>
    /// <param name="entity">The completed application hierarchy mapping.</param>
    /// <param name="mapping">The scalar role and optional flag descriptors.</param>
    /// <returns>The one writable fragment containing the structural roles.</returns>
    internal static StoreObjectIdentifier ValidatePlacement(
        IConventionEntityType entity,
        NestedSetModelDescriptor mapping
    )
    {
        var store = NestedSetStoreObject.Resolve(entity, mapping);
        if (mapping.AncestorIndexProperty is { } flag
            && flag
                .Resolve(entity)
                .GetOverrides()
                .Any(overrides => overrides.StoreObject != store
                    && (overrides.ColumnName is not null || !overrides.IsColumnNameOverridden)
                    && overrides.GetConfigurationSource() != ConfigurationSource.Convention))
        {
            // WHY: A table-specific override is an explicit physical placement, even without a renamed
            // column. A global column rename alone does not choose a fragment and remains relocatable.
            throw Invalid(entity, flag.Name, "cannot relocate an application-selected table fragment");
        }

        return store;
    }

    /// <summary>Removes unused convention-owned flags after obsolete library indexes have been reconciled.</summary>
    /// <param name="entity">The hierarchy whose optional property name may have changed.</param>
    internal static void RemoveObsoleteProperties(
        IConventionEntityType entity
    )
    {
        if (!s_owned.TryGetValue(entity, out var properties))
        {
            return;
        }

        foreach (var property in properties.ToArray())
        {
            if (!property.IsInModel)
            {
                properties.Remove(property);
                continue;
            }

            if (property.Name == Name(entity)
                || property.GetConfigurationSource() != ConfigurationSource.Convention
                || property.GetContainingIndexes().Any()
                || property.GetContainingKeys().Any()
                || property.GetContainingForeignKeys().Any()
                || property
                    .GetAnnotations()
                    .Any(annotation => annotation.GetConfigurationSource() != ConfigurationSource.Convention))
            {
                continue;
            }

            property.DeclaringType.RemoveProperty(property);
            properties.Remove(property);
        }
    }

    /// <summary>Reads the simple optional name that also survives runtime and compiled-model generation.</summary>
    /// <param name="entity">The configured hierarchy metadata.</param>
    /// <returns>The selected generated property name, or null when the index is not enabled.</returns>
    internal static string? Name(
        IReadOnlyEntityType entity
    )
    {
        var annotation = entity.FindAnnotation(NestedSetAnnotationNames.AncestorIndexProperty);
        if (annotation is null)
        {
            return null;
        }

        return annotation.Value is string name && !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' has invalid ancestor-index property metadata.");
    }

    /// <summary>Requires an ordinary required Boolean distinct from every writable structural role.</summary>
    /// <param name="entity">The hierarchy assigning the generated role.</param>
    /// <param name="property">The existing or convention-created candidate Boolean.</param>
    private static void ValidateType(
        IConventionEntityType entity,
        IConventionProperty property
    )
    {
        if (property.ClrType != typeof(bool)
            || property.IsNullable
            || property.GetValueConverter() is not null
            || property.GetProviderClrType() is not null
            || property.IsConcurrencyToken
            || property.GetContainingKeys().Any()
            || property.GetContainingForeignKeys().Any()
            || (entity.FindAnnotation(NestedSetAnnotationNames.OrderProperties)?.Value is string[] ordering
                && ordering.Contains(property.Name, StringComparer.Ordinal))
            || NestedSetPropertyRoles.All
                .Any(role => (entity.FindAnnotation(NestedSetAnnotationNames.Prefix + role)?.Value as string)
                == property.Name))
        {
            throw Invalid(
                entity,
                property.Name,
                "requires a separate required Boolean without conversion, identity, ordering, or concurrency-token semantics");
        }
    }

    /// <summary>Rejects application facets that could make the generated flag incorrect or writable.</summary>
    /// <param name="entity">The hierarchy whose flag is being finalized.</param>
    /// <param name="property">The generated Boolean metadata.</param>
    /// <param name="expression">The exact SQL derived from final boundary columns.</param>
    /// <param name="stored">The provider's qualified computed-storage policy.</param>
    private static void ValidateGeneration(
        IConventionEntityType entity,
        IConventionProperty property,
        string expression,
        bool stored
    )
    {
        var computed = property.FindAnnotation(RelationalAnnotationNames.ComputedColumnSql);
        var persistence = property.FindAnnotation(RelationalAnnotationNames.IsStored);

        if (property.FindAnnotation(RelationalAnnotationNames.DefaultValue) is not null
            || property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql) is not null
            || (computed is not null
                && computed.GetConfigurationSource() != ConfigurationSource.Convention
                && !Equals(computed.Value, expression))
            || (persistence is not null
                && persistence.GetConfigurationSource() != ConfigurationSource.Convention
                && !Equals(persistence.Value, stored))
            || (property.GetValueGeneratedConfigurationSource() is ConfigurationSource.Explicit
                    or ConfigurationSource.DataAnnotation
                && property.ValueGenerated != ValueGenerated.OnAddOrUpdate)
            || (property.GetBeforeSaveBehaviorConfigurationSource() is ConfigurationSource.Explicit
                    or ConfigurationSource.DataAnnotation
                && property.GetBeforeSaveBehavior() != PropertySaveBehavior.Ignore)
            || (property.GetAfterSaveBehaviorConfigurationSource() is ConfigurationSource.Explicit
                    or ConfigurationSource.DataAnnotation
                && property.GetAfterSaveBehavior() != PropertySaveBehavior.Ignore))
        {
            throw Invalid(
                entity,
                property.Name,
                "conflicts with the generated expression, storage, default, or save contract");
        }
    }

    /// <summary>Creates one stable model-building diagnostic for incompatible opt-in metadata.</summary>
    /// <param name="entity">The hierarchy rejecting the flag configuration.</param>
    /// <param name="property">The selected flag property name.</param>
    /// <param name="reason">The violated generation or mapping contract.</param>
    /// <returns>An exception identifying the exact entity, property, and violated contract.</returns>
    private static InvalidOperationException Invalid(
        IReadOnlyEntityType entity,
        string property,
        string reason
    ) => new($"Nested-set ancestor-index property '{property}' on '{entity.Name}' {reason}.");
}
