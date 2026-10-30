using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Adds nested-set conventions to each context's provider convention set.</summary>
internal sealed class NestedSetConventionSetPlugin : IConventionSetPlugin
{
    /// <summary>The active provider's SQL identifier service.</summary>
    private readonly ISqlGenerationHelper _sql;

    /// <summary>The selected native policy validated for this context scope.</summary>
    private readonly NestedSetProviderCapabilities _provider;

    /// <summary>The provider service resolving implicit property conversions before model finalization.</summary>
    private readonly ITypeMappingSource _typeMappings;

    /// <summary>Creates the convention plugin for one provider service graph.</summary>
    /// <param name="sql">The provider's SQL identifier service.</param>
    /// <param name="binding">The adapter validated against this context's completed options and native provider.</param>
    /// <param name="typeMappings">The provider's effective property-mapping service.</param>
    public NestedSetConventionSetPlugin(
        ISqlGenerationHelper sql,
        INestedSetProviderBinding binding,
        ITypeMappingSource typeMappings
    )
    {
        _sql = sql;
        _provider = binding.Provider;
        _typeMappings = typeMappings;
    }

    /// <inheritdoc />
    public ConventionSet ModifyConventions(
        ConventionSet conventionSet
    )
    {
        ArgumentNullException.ThrowIfNull(conventionSet);

        var indexConvention = new NestedSetIndexConvention(_sql, _provider, _typeMappings);

        conventionSet.ModelFinalizingConventions.Insert(0, new NestedSetAncestorIndexConvention(_provider));
        conventionSet.EntityTypePrimaryKeyChangedConventions.Add(indexConvention);
        conventionSet.ModelFinalizingConventions.Add(new NestedSetInfrastructureConvention(_provider, _typeMappings));
        conventionSet.ModelFinalizingConventions.Add(indexConvention);

        // WHY: Native finalizers run after common self-FK and index conventions complete their batches.
        _provider.AddModelFinalizers(conventionSet, _sql);

        return conventionSet;
    }
}
