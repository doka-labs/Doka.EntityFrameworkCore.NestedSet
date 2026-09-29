using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Adds nested-set conventions to each context's provider convention set.</summary>
internal sealed class NestedSetConventionSetPlugin : IConventionSetPlugin
{
    /// <summary>The active provider's SQL identifier service.</summary>
    private readonly ISqlGenerationHelper _sql;

    /// <summary>The official registered provider identity used to interpret provider-supported metadata.</summary>
    private readonly string _providerName;

    /// <summary>Creates the convention plugin for one provider service graph.</summary>
    /// <param name="sql">The provider's SQL identifier service.</param>
    /// <param name="provider">The official registered database provider.</param>
    public NestedSetConventionSetPlugin(
        ISqlGenerationHelper sql,
        IDatabaseProvider provider
    )
    {
        _sql = sql;
        _providerName = provider.Name;
    }

    /// <inheritdoc />
    public ConventionSet ModifyConventions(
        ConventionSet conventionSet
    )
    {
        ArgumentNullException.ThrowIfNull(conventionSet);

        var indexConvention = new NestedSetIndexConvention(_sql, _providerName);

        conventionSet.EntityTypePrimaryKeyChangedConventions.Add(indexConvention);
        conventionSet.ModelFinalizingConventions.Add(new NestedSetInfrastructureConvention(_providerName));
        conventionSet.ModelFinalizingConventions.Add(indexConvention);

        return conventionSet;
    }
}
