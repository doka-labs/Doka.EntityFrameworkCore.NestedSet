namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Partitions PostgreSQL's conventional nullable parent index into dependent and root access paths.</summary>
internal sealed class NestedSetParentIndexes : IModelFinalizingConvention
{
    /// <summary>The active provider's SQL identifier service.</summary>
    private readonly ISqlGenerationHelper _sql;

    /// <summary>Creates the PostgreSQL parent-index convention.</summary>
    /// <param name="sql">The active provider's SQL identifier service.</param>
    internal NestedSetParentIndexes(
        ISqlGenerationHelper sql
    )
    {
        _sql = sql;
    }

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            if (NestedSetModelValidator.IsNestedSet(entity))
            {
                var mapping = NestedSetModelValidator.Validate(entity, true)!;
                NestedSetIndexes.ApplyTreePredicate(entity, mapping, _sql);
                Reconcile(entity, mapping, _sql);
            }
        }
    }

    /// <summary>Filters only an untouched conventional self-FK index and adds its root counterpart.</summary>
    /// <param name="entity">The configured hierarchy entity with finalized relationship metadata.</param>
    /// <param name="mapping">The validated structural descriptor.</param>
    /// <param name="sql">The provider's identifier-delimiting service.</param>
    private static void Reconcile(
        IConventionEntityType entity,
        NestedSetModelDescriptor mapping,
        ISqlGenerationHelper sql
    )
    {
        var parent = mapping.Parent.Resolve(entity);
        var key = mapping.NodeKey.Resolve(entity);
        var dependent = mapping.Scope is null ? new[] { parent } : [mapping.Scope.Value.Resolve(entity), parent];
        var principal = mapping.Scope is null ? new[] { key } : [mapping.Scope.Value.Resolve(entity), key];
        var index = entity.FindIndex(dependent);

        if (!parent.IsNullable
            || index is null
            || index.DeclaringEntityType != entity
            || !IsUnmodified(index)
            || !entity
                .GetForeignKeys()
                .Any(foreignKey => foreignKey.PrincipalEntityType == entity
                    && foreignKey.Properties.SequenceEqual(dependent)
                    && foreignKey.PrincipalKey.Properties.SequenceEqual(principal)))
        {
            return;
        }

        var store = NestedSetStoreObject.Resolve(entity, mapping);
        var column = parent.GetColumnName(store)
            ?? throw new InvalidOperationException(
                $"Nested-set Parent property '{parent.Name}' on '{entity.Name}' is not mapped to its hierarchy table.");

        var parentSql = sql.DelimitIdentifier(column);

        // WHY: FK principal checks constrain Scope/NodeKey only. A Parent predicate excludes this dependent
        // index even when maintenance sees no committed rows and makes a scope-only scan appear cheaper.
        index.SetFilter(parentSql + " IS NOT NULL");

        // WHY: EF's FK index coverage convention ignores filters. Using the principal columns for roots
        // avoids it removing the dependent index as a duplicate of a same-key NULL companion.
        // WHY: EF maps inherited indexes by key columns, without inspecting the filter. The NULL Parent
        // tail keeps this index on the hierarchy table when TPT payload tables also contain NodeKey.
        var rootProperties = principal
            .Append(parent)
            .ToArray();

        var name = AvailableName(entity, store, principal, column);
        var roots = entity.AddIndex(rootProperties, name)
            ?? throw new InvalidOperationException("Another model convention rejected the nested-set root index.");

        roots.SetFilter(parentSql + " IS NULL");
        roots.SetDatabaseName(name);
    }

    /// <summary>Protects every application-adopted facet and specialized provider annotation.</summary>
    // WHY: This path adopts EF's anonymous self-FK index, not a NestedSet-owned structural index. Even existing
    // convention-level directions can belong to another convention; the stricter ownership test preserves them.
    private static bool IsUnmodified(
        IConventionIndex index
    ) => index.Name is null
        && !index.IsUnique
        && index.GetConfigurationSource() == ConfigurationSource.Convention
        && index.GetIsUniqueConfigurationSource() is null
        && index.GetIsDescendingConfigurationSource() is null
        && index
            .GetAnnotations()
            .All(annotation => annotation.Name == RelationalAnnotationNames.Name
                && annotation.GetConfigurationSource() == ConfigurationSource.Convention);

    /// <summary>Creates a stable provider-safe root-index identity from physical table and principal columns.</summary>
    private static string AvailableName(
        IConventionEntityType entity,
        StoreObjectIdentifier store,
        IReadOnlyList<IConventionProperty> principal,
        string parentColumn
    )
    {
        // WHY: Root companions use a physical identity and schema-wide collision checks. Ordinary model names
        // and structural physical names have different identity parts; sharing their builders would change DDL.
        var identity = string.Join(
            "\0",
            new[] { store.Schema, store.Name, parentColumn }.Concat(
                principal.Select(property => property.GetColumnName(store))));

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var stem = string.Concat("IX_NestedSet_Roots_", digest.AsSpan(0, 24));
        var name = stem;
        var suffix = 0;

        while (entity.FindIndex(name) is not null
               || entity
                   .Model
                   .GetEntityTypes()
                   .SelectMany(type => type.GetDeclaredIndexes())
                   .Any(index => index.GetDatabaseName() == name))
        {
            suffix++;
            name = stem + "_" + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }
}
