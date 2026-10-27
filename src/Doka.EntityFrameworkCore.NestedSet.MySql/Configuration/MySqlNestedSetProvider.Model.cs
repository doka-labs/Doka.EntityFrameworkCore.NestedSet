using Doka.EntityFrameworkCore.MySql;

namespace Doka.EntityFrameworkCore.NestedSet.MySql;

internal sealed partial class MySqlNestedSetProvider
{
    /// <inheritdoc />
    internal override bool UsesUnicodeScalarLength(
        RelationalTypeMapping mapping
    )
    {
        // WHY: Store spelling alone cannot authorize a custom mapping's character semantics. Exact concrete
        // identity preserves provider clones while rejecting subclasses and same-name foreign assemblies.
        if (mapping.GetType() != typeof(MySqlStringTypeMapping))
        {
            return false;
        }

        var storeType = mapping.StoreTypeNameBase;

        return storeType.Equals("varchar", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("char", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Finds the canonical Doka table annotation only on owners mapped to the exact physical table.</summary>
    /// <param name="model">The design model containing all possible shared physical-table owners.</param>
    /// <param name="store">The hierarchy's validated structural table.</param>
    /// <returns>The applicable table collation, or null when no owner declares one.</returns>
    /// <exception cref="InvalidOperationException">Mapped owners declare conflicting table values.</exception>
    internal override string? ResolveTableCollation(
        IReadOnlyModel model,
        StoreObjectIdentifier store
    )
    {
        string? collation = null;

        foreach (var owner in model.GetEntityTypes())
        {
            if (!MapsTable(owner, store)
                || owner.FindAnnotation(RelationalAnnotationNames.Collation)
                    ?.Value is not string candidate)
            {
                continue;
            }

            if (collation is not null
                && !string.Equals(collation, candidate, StringComparison.Ordinal))
            {
                // WHY: Identity semantics must not depend on which contradictory shared-table owner the
                // provider visits first. Reject the ambiguous table default when an identity needs it.
                throw new InvalidOperationException(
                    $"Nested-set structural table '{store}' has conflicting canonical table collations "
                    + $"'{collation}' and '{candidate}' on its mapped owners.");
            }

            collation = candidate;
        }

        return collation;
    }

    /// <summary>Tests primary and split-table mappings while retaining the original physical mapping owner.</summary>
    /// <param name="owner">A possible physical table owner.</param>
    /// <param name="store">The exact validated structural table.</param>
    /// <returns>Whether the owner is mapped to that physical table.</returns>
    private static bool MapsTable(
        IReadOnlyEntityType owner,
        StoreObjectIdentifier store
    )
    {
        if (owner.GetTableName() is null)
        {
            return false;
        }

        var strategy = owner.GetMappingStrategy();

        // WHY: EF retains the original TypeBase when a TPT mapping visits ancestor tables; Doka reads the table
        // annotation from that original owner. TPH and TPC stop at their own primary and explicit fragment maps.
        for (var mappedType = owner; mappedType is not null; mappedType = mappedType.BaseType)
        {
            if (mappedType.GetTableName() is { } table
                && (StoreObjectIdentifier.Table(table, mappedType.GetSchema()) == store
                    || mappedType
                        .GetMappingFragments(StoreObjectType.Table)
                        .Any(fragment => fragment.StoreObject == store)))
            {
                return true;
            }

            if (strategy is RelationalAnnotationNames.TpcMappingStrategy
                or RelationalAnnotationNames.TphMappingStrategy)
            {
                break;
            }
        }

        return false;
    }
}
