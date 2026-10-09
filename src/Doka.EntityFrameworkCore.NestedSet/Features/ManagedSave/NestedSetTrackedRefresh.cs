namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

/// <summary>Applies persisted structure and generated concurrency values to exact tracked entries.</summary>
internal static class NestedSetTrackedRefresh
{
    /// <summary>Accepts managed structure while preserving pending payload originals and generated sidecars.</summary>
    /// <param name="entry">The ordinary, derived, or named tracked entry being refreshed.</param>
    /// <param name="properties">The projected structural and generated concurrency properties.</param>
    /// <param name="row">The identity or ordinal followed by the projected property values.</param>
    internal static void Apply(
        EntityEntry entry,
        IReadOnlyList<IProperty> properties,
        object[] row
    )
    {
        var pendingPayload = entry.State == EntityState.Modified;
        IUpdateEntry? generated = null;

        for (var index = 0; index < properties.Count; index++)
        {
            var metadata = properties[index];

            if (!NestedSetRefreshProperties.AppliesTo(metadata, entry.Metadata))
            {
                continue;
            }

            if (pendingPayload
                && metadata.IsConcurrencyToken
                && (metadata.ValueGenerated & ValueGenerated.OnUpdate) != 0)
            {
                // WHY: EF preserves a pending token's original in its generated-value sidecar. Passing false
                // or clearing IsModified would overwrite that original or discard the newly generated value.
                generated ??= NestedSetInsertionTracking.EntryIdentity(entry);
                generated.SetStoreGeneratedValue(metadata, row[index + 1], setModified: true);

                continue;
            }

            // WHY: Bulk SQL bypasses EF. Accept managed values without turning unchanged siblings into writes.
            var property = NestedSetTrackedProperty.Property(entry, metadata);
            property.CurrentValue = row[index + 1];

            if (NestedSetTrackedProperty.HasOriginalValue(metadata))
            {
                property.OriginalValue = row[index + 1];
            }

            property.IsModified = false;
        }
    }
}
