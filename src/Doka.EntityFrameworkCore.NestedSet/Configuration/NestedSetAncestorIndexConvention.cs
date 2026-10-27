namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Completes derived ancestor-index declarations before provider finalizers budget index key widths.</summary>
internal sealed class NestedSetAncestorIndexConvention : IModelFinalizingConvention
{
    private readonly NestedSetProviderCapabilities _provider;

    /// <summary>Captures the native index policy used before provider key-width finalization.</summary>
    /// <param name="provider">The exact adapter selected by context options.</param>
    internal NestedSetAncestorIndexConvention(
        NestedSetProviderCapabilities provider
    )
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!NestedSetModelValidator.IsNestedSet(entity)
                || NestedSetAncestorIndex.Name(entity) is null)
            {
                continue;
            }

            // WHY: This stage sees the final application mapping without running after Doka's key-length
            // convention. The later hierarchy finalizer still derives SQL and validates physical co-location.
            NestedSetAncestorIndex.EnsureProperty(entity, true);
            if (NestedSetModelValidator.Validate(entity, false) is { } mapping)
            {
                NestedSetAncestorIndex.ValidatePlacement(entity, mapping);
            }

            NestedSetIndexes.Reconcile(entity, provider: _provider);
        }
    }
}
