namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

internal static partial class NestedSetModelValidator
{
    /// <summary>Resolves an explicit node key or adopts a scalar primary key by convention.</summary>
    /// <param name="entity">The configured entity.</param>
    /// <param name="required">Whether a missing identity must fail.</param>
    /// <returns>The scalar node-key property, or null while key conventions can still complete the model.</returns>
    private static IConventionProperty? ResolveNodeKey(
        IConventionEntityType entity,
        bool required
    )
    {
        var annotation = entity.FindAnnotation(NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.NodeKey);

        if (annotation?.Value is string configuredName)
        {
            var configured = entity.FindProperty(configuredName);
            if (configured is not null)
            {
                return configured;
            }

            if (required)
            {
                throw new InvalidOperationException(
                    $"Nested-set entity '{entity.Name}' requires a mapped NodeKey property.");
            }

            return null;
        }

        if (entity.FindPrimaryKey() is { Properties.Count: 1 } primaryKey)
        {
            var nodeKey = primaryKey.Properties[0];

            // WHY: Persisting the inferred role gives runtime and compiled-model consumers one metadata path.
            entity.SetAnnotation(NestedSetAnnotationNames.Prefix + NestedSetPropertyRoles.NodeKey, nodeKey.Name);

            return nodeKey;
        }

        if (required)
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' with a composite or missing primary key requires HasNodeKey "
                + "and a scalar alternate key.");
        }

        return null;
    }

    /// <summary>Resolves a role annotation and its mapped property.</summary>
    /// <param name="entity">The configured entity.</param>
    /// <param name="role">The structural role.</param>
    /// <param name="required">Whether absence is invalid at this model stage.</param>
    /// <returns>The mapped property, or null when the role is absent.</returns>
    private static IConventionProperty? Property(
        IConventionEntityType entity,
        string role,
        bool required
    )
    {
        var annotation = entity.FindAnnotation(NestedSetAnnotationNames.Prefix + role);
        var property = annotation?.Value is string name
            ? entity.FindProperty(name)
            : null;

        if (property is null && required)
        {
            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires a mapped {role} property.");
        }

        return property;
    }

    /// <summary>Requires a non-null scalar role.</summary>
    /// <param name="property">The property fulfilling the role.</param>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="role">The structural role name.</param>
    private static void Required(
        IConventionProperty property,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.IsNullable)
        {
            throw new InvalidOperationException(
                $"Nested-set {role} property '{property.Name}' on '{entity.Name}' must be required.");
        }
    }

    /// <summary>Requires one exact non-null CLR storage type for an arithmetic structural role.</summary>
    /// <param name="property">The property fulfilling the role.</param>
    /// <param name="type">The required CLR type.</param>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="role">The structural role name.</param>
    private static void RequiredType(
        IConventionProperty property,
        Type type,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.ClrType != type
            || property.IsNullable)
        {
            throw new InvalidOperationException(
                $"Nested-set {role} property '{property.Name}' on '{entity.Name}' must be required {type.Name}.");
        }
    }

    /// <summary>Rejects provider conversions that would change numeric SQL arithmetic semantics.</summary>
    private static void RequireUnconvertedArithmetic(
        IConventionProperty property,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.GetValueConverter() is not null
            || property.GetProviderClrType() is not null)
        {
            throw new InvalidOperationException(
                $"Nested-set arithmetic property '{property.Name}' for {role} on '{entity.Name}' "
                + "must use its native numeric store representation.");
        }
    }

    /// <summary>Requires NodeKey and Parent to retain the same relational equality representation.</summary>
    private static void RequireCompatibleIdentity(
        IConventionProperty nodeKey,
        IConventionProperty parent,
        IConventionEntityType entity
    )
    {
        var keyProvider = ProviderType(nodeKey);
        var parentProvider = ProviderType(parent);

        if (keyProvider != parentProvider
            || nodeKey.GetColumnType() != parent.GetColumnType()
            || nodeKey.GetMaxLength() != parent.GetMaxLength()
            || nodeKey.IsUnicode() != parent.IsUnicode()
            || nodeKey.IsFixedLength() != parent.IsFixedLength()
            || nodeKey.GetPrecision() != parent.GetPrecision()
            || nodeKey.GetScale() != parent.GetScale())
        {
            throw new InvalidOperationException(
                $"Nested-set NodeKey '{nodeKey.Name}' and Parent '{parent.Name}' on '{entity.Name}' "
                + "must use compatible relational mappings.");
        }
    }

    /// <summary>Returns the non-null provider type configured before relational type mapping is initialized.</summary>
    private static Type ProviderType(
        IConventionProperty property
    )
    {
        var converter = property.GetValueConverter();
        var provider = property.GetProviderClrType() ?? converter?.ProviderClrType ?? property.ClrType;

        return Nullable.GetUnderlyingType(provider) ?? provider;
    }

    /// <summary>Requires a hierarchy-owned role to remain writable throughout the entity lifetime.</summary>
    /// <param name="property">The property fulfilling the role.</param>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="role">The structural role name.</param>
    private static void Writable(
        IConventionProperty property,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.ValueGenerated != ValueGenerated.Never
            || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save
            || property.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
        {
            throw new InvalidOperationException(
                $"Nested-set structural property '{property.Name}' for {role} on '{entity.Name}' must be writable "
                + "and not generated.");
        }
    }

    /// <summary>Requires an immutable partition identity to accept an application value during insertion.</summary>
    /// <param name="property">The property fulfilling the role.</param>
    /// <param name="entity">The declaring entity.</param>
    /// <param name="role">The structural role name.</param>
    private static void AssignableOnInsert(
        IConventionProperty property,
        IConventionEntityType entity,
        string role
    )
    {
        if (property.ValueGenerated != ValueGenerated.Never
            || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save)
        {
            throw new InvalidOperationException(
                $"Nested-set structural property '{property.Name}' for {role} on '{entity.Name}' must accept an "
                + "assigned value and not be generated.");
        }

        // WHY: Scope is an immutable partition identity and commonly belongs to an EF key. EF therefore uses Throw
        // after insertion, while nested-set operations only need to assign it before the initial save.
    }

    /// <summary>Requires the node key to be protected by a scalar or scope-qualified EF key.</summary>
    /// <param name="entity">The configured entity.</param>
    /// <param name="nodeKey">The selected scalar node key.</param>
    /// <param name="scope">The optional scope property.</param>
    private static void ValidateIdentity(
        IConventionEntityType entity,
        IConventionProperty nodeKey,
        IConventionProperty? scope
    )
    {
        var hasScalarIdentity = entity
            .GetKeys()
            .Any(key => key.Properties.Count == 1 && key.Properties[0] == nodeKey);

        var hasScopedIdentity = scope is not null
            && entity
                .GetKeys()
                .Any(key => key.Properties.Count == 2 && key.Properties[0] == scope && key.Properties[1] == nodeKey);

        if (!hasScalarIdentity
            && !hasScopedIdentity)
        {
            var expected = scope is null
                ? "NodeKey"
                : "NodeKey or (Scope, NodeKey)";

            throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' requires an EF primary or alternate key over {expected}.");
        }
    }

    /// <summary>Returns the provider-comparison model type after applying any value converter.</summary>
    /// <param name="property">The property whose model type participates in role compatibility.</param>
    /// <returns>The non-nullable CLR model type.</returns>
    private static Type ModelType(
        IConventionProperty property
    ) => Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
}
