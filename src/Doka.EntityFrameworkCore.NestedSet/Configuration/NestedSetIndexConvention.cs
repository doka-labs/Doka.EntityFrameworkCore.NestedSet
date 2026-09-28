namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Maintains indexes while metadata is mutable and validates the completed node-identity contract.</summary>
internal sealed class NestedSetIndexConvention : IEntityTypePrimaryKeyChangedConvention, IModelFinalizingConvention
{
    /// <summary>The active provider's SQL identifier service.</summary>
    private readonly ISqlGenerationHelper _sql;

    /// <summary>The registered provider identity controlling canonical table collation support.</summary>
    private readonly string _providerName;

    /// <summary>Creates the hierarchy convention for one provider service graph.</summary>
    /// <param name="sql">The provider's SQL identifier service.</param>
    /// <param name="providerName">The official registered database provider name.</param>
    internal NestedSetIndexConvention(
        ISqlGenerationHelper sql,
        string providerName
    )
    {
        _sql = sql;
        _providerName = providerName;
    }

    /// <inheritdoc />
    public void ProcessEntityTypePrimaryKeyChanged(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionKey? newPrimaryKey,
        IConventionKey? previousPrimaryKey,
        IConventionContext<IConventionKey> context
    ) =>
        // WHY: Waiting solely for finalization would add indexes after provider-specific index-length budgeting.
        NestedSetIndexes.Reconcile(entityTypeBuilder.Metadata);

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
            NestedSetIndexes.Reconcile(entity);
            NestedSetConstraints.Reconcile(entity, mapping, _sql);
        }

        NestedSetCollations.Capture(modelBuilder.Metadata, _providerName);
    }
}
