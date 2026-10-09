using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Adds nested-set conventions to each context's provider convention set.</summary>
internal sealed class NestedSetConventionSetPlugin : IConventionSetPlugin
{
    /// <summary>The active provider's SQL identifier service.</summary>
    private readonly ISqlGenerationHelper _sql;

    /// <summary>The official registered provider identity used to interpret provider-supported metadata.</summary>
    private readonly string _providerName;

    /// <summary>The provider service resolving implicit property conversions before model finalization.</summary>
    private readonly ITypeMappingSource _typeMappings;

    /// <summary>Creates the convention plugin for one provider service graph.</summary>
    /// <param name="sql">The provider's SQL identifier service.</param>
    /// <param name="provider">The official registered database provider.</param>
    /// <param name="typeMappings">The provider's effective property-mapping service.</param>
    public NestedSetConventionSetPlugin(
        ISqlGenerationHelper sql,
        IDatabaseProvider provider,
        ITypeMappingSource typeMappings
    )
    {
        _sql = sql;
        _providerName = provider.Name;
        _typeMappings = typeMappings;
    }

    /// <inheritdoc />
    public ConventionSet ModifyConventions(
        ConventionSet conventionSet
    )
    {
        ArgumentNullException.ThrowIfNull(conventionSet);

        var indexConvention = new NestedSetIndexConvention(_sql, _providerName, _typeMappings);

        conventionSet.EntityTypePrimaryKeyChangedConventions.Add(indexConvention);
        conventionSet.ModelFinalizingConventions.Add(
            new NestedSetInfrastructureConvention(_providerName, _typeMappings));
        conventionSet.ModelFinalizingConventions.Add(indexConvention);

        if (_providerName == NestedSetProviderCapabilities.PostgreSqlProviderName)
        {
            // WHY: EF drains relationship conventions after each finalizer. The parent index exists only
            // after the preceding nested-set finalizer's new self-FK has completed that batch.
            conventionSet.ModelFinalizingConventions.Add(new NestedSetParentIndexes(_sql));
        }

        return conventionSet;
    }
}
