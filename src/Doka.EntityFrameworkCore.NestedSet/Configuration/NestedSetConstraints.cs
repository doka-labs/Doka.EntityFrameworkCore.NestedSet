namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Derives portable structural checks and the unambiguous nested-set parent relationship.</summary>
internal static class NestedSetConstraints
{
    /// <summary>Creates or validates the finalized database constraint metadata.</summary>
    /// <param name="entity">The finalized mutable entity metadata.</param>
    /// <param name="mapping">The validated hierarchy descriptor.</param>
    /// <param name="sql">The active provider's identifier-delimiting service.</param>
    internal static void Reconcile(
        IConventionEntityType entity,
        NestedSetModelDescriptor mapping,
        ISqlGenerationHelper sql
    )
    {
        var store = NestedSetStoreObject.Resolve(entity, mapping);

        var left = Column(mapping.Left, store, entity);
        var right = Column(mapping.Right, store, entity);
        var depth = Column(mapping.Depth, store, entity);
        var position = Column(mapping.Position, store, entity);

        if (StoreObjectIdentifier.Create(entity, StoreObjectType.Table) == store)
        {
            EnsureCheck(entity, store, "LeftMin", $"{sql.DelimitIdentifier(left)} >= 1");
            EnsureCheck(
                entity,
                store,
                "RightAfterLeft",
                $"{sql.DelimitIdentifier(right)} > {sql.DelimitIdentifier(left)}");
            EnsureCheck(entity, store, "DepthMin", $"{sql.DelimitIdentifier(depth)} >= 0");
            EnsureCheck(entity, store, "PositionMin", $"{sql.DelimitIdentifier(position)} >= 0");
        }

        // WHY: EF Core exposes check constraints on TableBuilder but not SplitTableBuilder. Attaching the SQL to
        // the entity when structure lives in a secondary split fragment targets the main table and breaks DDL.
        // Runtime hierarchy validation remains authoritative for this EF model shape.
        EnsureParentForeignKey(entity, mapping);
    }

    /// <summary>Returns one required physical column name from the validated write fragment.</summary>
    private static string Column(
        NestedSetPropertyDescriptor property,
        StoreObjectIdentifier store,
        IConventionEntityType entity
    )
    {
        var metadata = property.Resolve(entity);

        return metadata.GetColumnName(store)
            ?? throw new InvalidOperationException(
                $"Nested-set property '{property.Name}' on '{entity.Name}' is not mapped to the hierarchy table.");
    }

    /// <summary>Adds one provider-delimited check unless the application already supplied the same guarantee.</summary>
    private static void EnsureCheck(
        IConventionEntityType entity,
        StoreObjectIdentifier store,
        string role,
        string expression
    )
    {
        if (entity
            .GetCheckConstraints()
            .Any(constraint => constraint.Sql == expression))
        {
            return;
        }

        var name = ConstraintName(store, role);
        if (entity.FindCheckConstraint(name) is { } conflicting)
        {
            throw new InvalidOperationException(
                $"Nested-set check constraint '{name}' conflicts with application SQL '{conflicting.Sql}'.");
        }

        var configured = entity.Builder.HasCheckConstraint(name, expression, false)
            ?? throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' rejected required check constraint '{name}'.");
    }

    /// <summary>Creates a scope-aware self-FK without coupling parent identity to the movable TreeId.</summary>
    private static void EnsureParentForeignKey(
        IConventionEntityType entity,
        NestedSetModelDescriptor mapping
    )
    {
        var parent = mapping.Parent.Resolve(entity);
        var nodeKey = mapping.NodeKey.Resolve(entity);
        var dependent = mapping.Scope is null
            ? new[] { parent }
            : new[] { mapping.Scope.Value.Resolve(entity), parent };

        var principal = mapping.Scope is null
            ? new[] { nodeKey }
            : new[] { mapping.Scope.Value.Resolve(entity), nodeKey };

        var principalKey = entity
                .GetKeys()
                .SingleOrDefault(key => key.Properties.SequenceEqual(principal))
            // WHY: EF inheritance keys belong to root metadata even when an independently configured concrete
            // TPC hierarchy needs a scope-qualified parent key over properties inherited from that root.
            ?? entity
                .GetRootType()
                .Builder
                .HasKey(principal, false)
                ?.Metadata
            ?? throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' rejected its required parent identity key.");

        var candidates = entity
            .GetForeignKeys()
            .Where(foreignKey => foreignKey.Properties.Contains(parent))
            .ToArray();

        if (candidates.Length == 0
            && !CanRepresentRelationshipOnEveryMappedTable(entity, parent, nodeKey))
        {
            // WHY: EF models a self-FK at entity-type level. With entity splitting it reports that relationship
            // against every mapped table, even when Parent and NodeKey share one writable structure fragment.
            // The library still enforces parent identity; emitting an EF FK here would make the model invalid.
            return;
        }

        if (!dependent
                .Zip(principal, RelationallyCompatible)
                .All(compatible => compatible))
        {
            if (candidates.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Nested-set Parent relationship on '{entity.Name}' uses incompatible relational mappings.");
            }

            // WHY: MySQL, MariaDB, and SQL Server reject an FK whose store facets or collations differ. The
            // library-level parent comparison remains valid, while emitting an unusable database FK would break DDL.
            return;
        }

        var exact = candidates
            .Where(foreignKey => foreignKey.PrincipalEntityType == entity
                && foreignKey.PrincipalKey == principalKey
                && foreignKey.Properties.SequenceEqual(dependent))
            .ToArray();

        if (exact.Length > 1
            || candidates.Any(foreignKey => !exact.Contains(foreignKey)))
        {
            throw new InvalidOperationException(
                $"Nested-set Parent property '{mapping.Parent.Name}' on '{entity.Name}' "
                + "has an ambiguous relationship.");
        }

        var relationship = exact.SingleOrDefault()
                ?.Builder
            ?? entity.Builder.HasRelationship(entity, dependent, principalKey, false)
            ?? throw new InvalidOperationException(
                $"Nested-set entity '{entity.Name}' rejected its required parent relationship.");

        if (relationship.Metadata.DeleteBehavior is not DeleteBehavior.Restrict and not DeleteBehavior.NoAction)
        {
            // WHY: EF applies ClientSetNull after a convention-created optional FK. Data-annotation precedence
            // replaces that default while still yielding to explicit Cascade, ClientCascade, or ClientSetNull.
            if (relationship.OnDelete(DeleteBehavior.Restrict, true) is null
                || relationship.Metadata.DeleteBehavior is not DeleteBehavior.Restrict and not DeleteBehavior.NoAction)
            {
                throw new InvalidOperationException(
                    $"Nested-set parent relationship on '{entity.Name}' requires Restrict or NoAction deletion.");
            }
        }
    }

    /// <summary>Checks whether EF can place one conceptual self-FK on every table carrying the principal key.</summary>
    private static bool CanRepresentRelationshipOnEveryMappedTable(
        IConventionEntityType entity,
        IConventionProperty parent,
        IConventionProperty nodeKey
    )
    {
        var tables = new List<StoreObjectIdentifier>();

        if (entity.GetTableName() is { } table)
        {
            tables.Add(StoreObjectIdentifier.Table(table, entity.GetSchema()));
        }

        tables.AddRange(
            entity
                .GetMappingFragments(StoreObjectType.Table)
                .Select(fragment => fragment.StoreObject));

        return tables
            .Distinct()
            .Where(store => nodeKey.GetColumnName(store) is not null)
            .All(store => parent.GetColumnName(store) is not null);
    }

    /// <summary>Determines whether a dependent/principal property pair can back one portable relational FK.</summary>
    private static bool RelationallyCompatible(
        IConventionProperty dependent,
        IConventionProperty principal
    )
    {
        var dependentConverter = dependent.GetValueConverter();
        var principalConverter = principal.GetValueConverter();
        var dependentProviderType = dependent.GetProviderClrType()
            ?? dependentConverter?.ProviderClrType ?? dependent.ClrType;

        var principalProviderType = principal.GetProviderClrType()
            ?? principalConverter?.ProviderClrType ?? principal.ClrType;

        // WHY: Type mappings are initialized after ModelFinalizing. These public configuration facets are the
        // complete provider input available while the convention can still add a relationship.
        return NonNullableType(dependentProviderType) == NonNullableType(principalProviderType)
            && dependent.GetColumnType() == principal.GetColumnType()
            && dependent.GetMaxLength() == principal.GetMaxLength()
            && dependent.IsUnicode() == principal.IsUnicode()
            && dependent.IsFixedLength() == principal.IsFixedLength()
            && dependent.GetPrecision() == principal.GetPrecision()
            && dependent.GetScale() == principal.GetScale()
            && dependent.GetCollation() == principal.GetCollation();
    }

    /// <summary>Normalizes optional parent provider types to the required node-key provider type.</summary>
    private static Type NonNullableType(
        Type type
    ) => Nullable.GetUnderlyingType(type) ?? type;

    /// <summary>Builds a stable short model name from the physical table and invariant role.</summary>
    private static string ConstraintName(
        StoreObjectIdentifier store,
        string role
    )
    {
        var identity = string.Join("\0", store.Schema, store.Name, role);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

        return $"CK_NestedSet_{role}_{digest[..12]}";
    }
}
