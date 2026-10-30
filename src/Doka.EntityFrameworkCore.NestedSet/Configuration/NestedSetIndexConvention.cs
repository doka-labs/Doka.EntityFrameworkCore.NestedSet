namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Maintains indexes while metadata is mutable and validates the completed node-identity contract.</summary>
internal sealed class NestedSetIndexConvention : IEntityTypePrimaryKeyChangedConvention, IModelFinalizingConvention
{
    /// <summary>The active provider's SQL identifier service.</summary>
    private readonly ISqlGenerationHelper _sql;

    /// <summary>The registered provider identity controlling collation support.</summary>
    private readonly NestedSetProviderCapabilities _provider;

    /// <summary>The provider service resolving implicit conversions from configured store facets.</summary>
    private readonly ITypeMappingSource _typeMappings;

    /// <summary>Creates the hierarchy convention for one provider service graph.</summary>
    /// <param name="sql">The provider's SQL identifier service.</param>
    /// <param name="provider">The selected native provider implementation.</param>
    /// <param name="typeMappings">The provider's effective property-mapping service.</param>
    internal NestedSetIndexConvention(
        ISqlGenerationHelper sql,
        NestedSetProviderCapabilities provider,
        ITypeMappingSource typeMappings
    )
    {
        _sql = sql;
        _provider = provider;
        _typeMappings = typeMappings;
    }

    /// <inheritdoc />
    public void ProcessEntityTypePrimaryKeyChanged(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionKey? newPrimaryKey,
        IConventionKey? previousPrimaryKey,
        IConventionContext<IConventionKey> context
    ) =>
        // WHY: Waiting solely for finalization would add indexes after provider-specific index-length budgeting.
        NestedSetIndexes.Reconcile(entityTypeBuilder.Metadata, provider: _provider);

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!NestedSetModelValidator.IsNestedSet(entity))
            {
                continue;
            }

            // WHY: Late application indexes can replace equivalent convention-owned paths without duplicate DDL.
            var mapping = NestedSetModelValidator.Validate(entity, true)!;
            NestedSetAncestorIndex.Reconcile(entity, mapping, _sql, _provider, _typeMappings);
            NestedSetIndexes.Reconcile(entity, provider: _provider);
            NestedSetAncestorIndex.RemoveObsoleteProperties(entity);
            NestedSetConstraints.Reconcile(entity, mapping, _sql);
        }

        NestedSetCollations.Capture(modelBuilder.Metadata, _provider, _typeMappings);
    }
}
